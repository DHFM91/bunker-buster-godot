using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// The heads-up display, laid out like the browser game's HUD: dark panels with a mint top edge
    /// in bold monospace. Top left: level and objective. Top centre: compass heading, the objective
    /// arrow and distance, and the alert bar. Top right: location. Bottom left: vitals. Bottom right:
    /// carbine / breach kit (with the grenade key reminder). Centre: reticle, plant prompt, messages.
    /// </summary>
    public partial class Hud : CanvasLayer
    {
        static readonly Color Amber = new Color("f2b76b"), Mint = new Color("a9d9bd"), Ink = new Color("0b1414"),
            Paper = new Color("e5e8d9"), MessageColor = new Color("ffe0a8");
        static readonly Color[] AlertColors = { Colors.White, new Color("ffd54a"), new Color("ff9a3c"), new Color("ff4a3a") };
        static readonly Color[] AlertBacks = { Colors.Transparent, new Color(.23f, .2f, .04f, .6f), new Color(.24f, .13f, .04f, .6f), new Color(.29f, .06f, .04f, .69f) };

        Control root, overlay;
        Label levelLabel, objective, bearing, signal, location, health, kit, message, overlayTitle, overlayBody, overlayHint, prompt, debug;
        Label alertName, alertTime;
        PanelContainer alertBar;
        StyleBoxFlat alertStyle;
        ColorRect alertFill, healthFill, chargeFill, hurtTint, vignette;
        Control chargeMeter;
        ObjectiveArrow arrow;
        double messageTime, flashClock;
        int alertLevel;

        static Font Mono()
        {
            var f = new SystemFont { FontNames = new[] { "Consolas", "Cascadia Mono", "Courier New", "monospace" }, FontWeight = 700 };
            return f;
        }

        Font mono;

        Label MakeLabel(int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var l = new Label { HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", mono);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            l.AddThemeColorOverride("font_shadow_color", new Color(.01f, .03f, .03f, .9f));
            l.AddThemeConstantOverride("shadow_offset_y", 1);
            l.AddThemeConstantOverride("shadow_outline_size", 3);
            return l;
        }

        static StyleBoxFlat PanelStyle()
        {
            var s = new StyleBoxFlat { BgColor = new Color(Ink, .85f), BorderColor = new Color(Mint, .44f) };
            s.BorderWidthTop = 1;
            s.ContentMarginLeft = 14; s.ContentMarginRight = 14; s.ContentMarginTop = 10; s.ContentMarginBottom = 10;
            s.ShadowColor = new Color(.02f, .06f, .06f, .25f); s.ShadowSize = 6;
            return s;
        }

        PanelContainer Panel(VBoxContainer box)
        {
            var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(150, 0) };
            p.AddThemeStyleboxOverride("panel", PanelStyle());
            box.AddThemeConstantOverride("separation", 4);
            box.MouseFilter = Control.MouseFilterEnum.Ignore;
            p.AddChild(box);
            return p;
        }

        Label SmallLabel(string text, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var l = MakeLabel(12, Mint, align);
            l.Text = text;
            return l;
        }

        public override void _Ready()
        {
            mono = Mono();
            root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(root);

            // edge darkening like the browser HUD's radial vignette, and the red damage flash
            vignette = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = new Color(0, 0, 0, 0) };
            vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            vignette.Material = new ShaderMaterial
            {
                Shader = new Shader
                {
                    Code = "shader_type canvas_item;\nvoid fragment(){ vec2 d = UV - 0.5; float r = length(d * vec2(1.0, 0.75)); COLOR = vec4(0.02, 0.04, 0.04, smoothstep(0.32, 0.62, r) * 0.28); }",
                },
            };
            root.AddChild(vignette);
            hurtTint = new ColorRect { Color = new Color(.7f, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            hurtTint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(hurtTint);

            var frame = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) frame.AddThemeConstantOverride(side, 26);
            root.AddChild(frame);
            var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            frame.AddChild(rows);

            // ---- top row ----
            var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            rows.AddChild(top);
            var leftBox = new VBoxContainer();
            levelLabel = SmallLabel("LEVEL 1");
            objective = MakeLabel(17, Paper);
            leftBox.AddChild(levelLabel);
            leftBox.AddChild(objective);
            var leftPanel = Panel(leftBox);
            leftPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            top.AddChild(leftPanel);
            top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

            var centerBox = new VBoxContainer();
            bearing = MakeLabel(17, Paper, HorizontalAlignment.Center);
            var signalRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            signalRow.AddThemeConstantOverride("separation", 10);
            arrow = new ObjectiveArrow { CustomMinimumSize = new Vector2(30, 30), MouseFilter = Control.MouseFilterEnum.Ignore };
            signal = MakeLabel(14, Amber);
            signalRow.AddChild(arrow);
            signalRow.AddChild(signal);
            centerBox.AddChild(bearing);
            centerBox.AddChild(signalRow);
            alertBar = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            alertStyle = new StyleBoxFlat { BorderColor = Colors.White, BgColor = AlertBacks[1] };
            alertStyle.SetBorderWidthAll(1);
            alertStyle.ContentMarginLeft = 8; alertStyle.ContentMarginRight = 8; alertStyle.ContentMarginTop = 3; alertStyle.ContentMarginBottom = 6;
            alertBar.AddThemeStyleboxOverride("panel", alertStyle);
            var alertInner = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(230, 24) };
            alertName = MakeLabel(16, Colors.White);
            alertTime = MakeLabel(16, Colors.White, HorizontalAlignment.Right);
            alertTime.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            alertTime.GrowHorizontal = Control.GrowDirection.Begin;
            alertFill = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(230, 3), Position = new Vector2(0, 24) };
            alertInner.AddChild(alertName);
            alertInner.AddChild(alertTime);
            alertInner.AddChild(alertFill);
            alertBar.AddChild(alertInner);
            centerBox.AddChild(alertBar);
            var centerPanel = Panel(centerBox);
            centerPanel.CustomMinimumSize = new Vector2(260, 0);
            centerPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            top.AddChild(centerPanel);

            top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
            var rightBox = new VBoxContainer();
            rightBox.AddChild(SmallLabel("LOCATION", HorizontalAlignment.Right));
            location = MakeLabel(17, Paper, HorizontalAlignment.Right);
            rightBox.AddChild(location);
            var rightPanel = Panel(rightBox);
            rightPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            top.AddChild(rightPanel);

            // ---- middle: status message and charge meter near the bottom centre ----
            var middle = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
            rows.AddChild(middle);
            message = MakeLabel(19, MessageColor, HorizontalAlignment.Center);
            middle.AddChild(message);
            chargeMeter = new ColorRect { Color = new Color(0, 0, 0, .55f), CustomMinimumSize = new Vector2(220, 5), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            chargeFill = new ColorRect { Color = Amber, Size = new Vector2(0, 5), MouseFilter = Control.MouseFilterEnum.Ignore };
            chargeMeter.AddChild(chargeFill);
            middle.AddChild(chargeMeter);
            middle.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14), MouseFilter = Control.MouseFilterEnum.Ignore });

            // ---- bottom row ----
            var bottom = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            rows.AddChild(bottom);
            var vitals = new VBoxContainer();
            vitals.AddChild(SmallLabel("VITALS"));
            health = MakeLabel(17, Paper);
            vitals.AddChild(health);
            var meter = new ColorRect { Color = new Color(1, 1, 1, .12f), CustomMinimumSize = new Vector2(160, 4), MouseFilter = Control.MouseFilterEnum.Ignore };
            healthFill = new ColorRect { Color = Mint, Size = new Vector2(160, 4), MouseFilter = Control.MouseFilterEnum.Ignore };
            meter.AddChild(healthFill);
            vitals.AddChild(meter);
            var vitalsPanel = Panel(vitals);
            vitalsPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            bottom.AddChild(vitalsPanel);
            debug = MakeLabel(11, new Color(Paper, .45f));
            debug.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            debug.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            debug.HorizontalAlignment = HorizontalAlignment.Center;
            bottom.AddChild(debug);
            var kitBox = new VBoxContainer();
            kitBox.AddChild(SmallLabel("CARBINE / BREACH KIT", HorizontalAlignment.Right));
            kit = MakeLabel(17, Paper, HorizontalAlignment.Right);
            kitBox.AddChild(kit);
            var kitPanel = Panel(kitBox);
            kitPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            bottom.AddChild(kitPanel);

            // ---- reticle and plant prompt, centred on screen ----
            var reticle = new Reticle { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(40, 10) };
            reticle.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
            root.AddChild(reticle);
            var promptPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            var ps = new StyleBoxFlat { BgColor = new Color(.067f, .1f, .09f, .87f), BorderColor = new Color(Amber, .4f) };
            ps.SetBorderWidthAll(1);
            ps.ContentMarginLeft = 12; ps.ContentMarginRight = 12; ps.ContentMarginTop = 6; ps.ContentMarginBottom = 6;
            promptPanel.AddThemeStyleboxOverride("panel", ps);
            prompt = MakeLabel(15, new Color("ffdc9e"));
            promptPanel.AddChild(prompt);
            promptPanel.AnchorLeft = .5f; promptPanel.AnchorRight = .5f; promptPanel.AnchorTop = .58f; promptPanel.AnchorBottom = .58f;
            promptPanel.GrowHorizontal = Control.GrowDirection.Both;
            root.AddChild(promptPanel);
            promptBox = promptPanel;

            // ---- overlay for briefings, pause and results ----
            overlay = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, .72f) };
            style.SetContentMarginAll(40);
            ((PanelContainer)overlay).AddThemeStyleboxOverride("panel", style);
            overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", 18);
            overlay.AddChild(box);
            overlayTitle = MakeLabel(52, Paper, HorizontalAlignment.Center);
            overlayBody = MakeLabel(18, Paper, HorizontalAlignment.Center);
            overlayBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            overlayBody.CustomMinimumSize = new Vector2(760, 0);
            overlayBody.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            overlayHint = MakeLabel(16, Mint, HorizontalAlignment.Center);
            box.AddChild(overlayTitle);
            box.AddChild(overlayBody);
            box.AddChild(overlayHint);
            root.AddChild(overlay);
        }

        PanelContainer promptBox;

        // ---------- updates from the game ----------

        public void SetLevel(int level, string objectiveText, string locationName)
        {
            levelLabel.Text = "LEVEL " + level;
            objective.Text = objectiveText;
            location.Text = locationName;
        }

        /// <summary>Compass: heading in degrees (0 = the way you faced at spawn), arrow angle (0 = straight ahead, + = clockwise), distance line.</summary>
        public void SetCompass(double headingDeg, float arrowAngle, string line)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            double h = (headingDeg % 360 + 360) % 360;
            bearing.Text = names[(int)Mathf.Round((float)(h / 45)) % 8] + " / " + ((int)h).ToString("000") + "°";
            arrow.Angle = arrowAngle;
            signal.Text = line;
        }

        /// <summary>Alert phase readout: hidden when undetected, otherwise name, seconds left and a draining bar.</summary>
        public void SetAlert(int level, string name, double timer, double fraction)
        {
            alertLevel = level;
            alertBar.Visible = level > 0;
            if (level == 0) return;
            alertName.Text = name;
            alertTime.Text = timer.ToString("0.0");
            var c = AlertColors[level];
            alertName.AddThemeColorOverride("font_color", c);
            alertTime.AddThemeColorOverride("font_color", c);
            alertStyle.BorderColor = c;
            alertFill.Color = c;
            alertFill.Size = new Vector2(230 * (float)Mathf.Clamp(fraction, 0, 1), 3);
        }

        public void SetVitals(int hp, double hurt)
        {
            health.Text = hp.ToString("00") + " HP";
            healthFill.Size = new Vector2(160 * hp / 100f, 4);
            healthFill.Color = hp <= 36 ? new Color("ff6a4a") : Mint;
            hurtTint.Color = new Color(.7f, 0, 0, (float)Mathf.Clamp(hurt * .7, 0, .45));
        }

        public void SetKit(string text) => kit.Text = text;

        public void SetDebug(string text) => debug.Text = text;

        /// <summary>Shows a key prompt such as "Q  PLANT CHARGE" (empty hides it).</summary>
        public void SetPrompt(string key, string action)
        {
            promptBox.Visible = !string.IsNullOrEmpty(key);
            if (promptBox.Visible) prompt.Text = key + "   " + action;
        }

        /// <summary>Planting / fuse progress bar under the message (fraction &lt; 0 hides it).</summary>
        public void SetChargeMeter(float fraction)
        {
            chargeMeter.Visible = fraction >= 0;
            if (fraction >= 0) chargeFill.Size = new Vector2(220 * Mathf.Clamp(fraction, 0, 1), 5);
        }

        /// <summary>A steady status line (charge armed, planting) that overrides timed messages.</summary>
        public string Status;

        public void Say(string text, double seconds = 2.5)
        {
            message.Text = text;
            messageTime = seconds;
        }

        public void ShowOverlay(string title, string body, string hint)
        {
            overlayTitle.Text = title;
            overlayBody.Text = body;
            overlayHint.Text = hint;
            overlay.Visible = true;
        }

        public void HideOverlay() => overlay.Visible = false;

        public override void _Process(double delta)
        {
            flashClock += delta;
            if (!string.IsNullOrEmpty(Status)) message.Text = Status;
            else if (messageTime > 0)
            {
                messageTime -= delta;
                if (messageTime <= 0) message.Text = "";
            }
            else message.Text = "";
            // ALERT flashes like the browser version
            if (alertLevel == 3) alertStyle.BgColor = (int)(flashClock * 4) % 2 == 0 ? AlertBacks[3] : new Color(.48f, .09f, .06f, .82f);
            else if (alertLevel > 0) alertStyle.BgColor = AlertBacks[alertLevel];
        }
    }

    /// <summary>The objective pointer: an amber arrow with a head and shaft, rotated toward the target.</summary>
    public partial class ObjectiveArrow : Control
    {
        float angle;
        public float Angle { get => angle; set { angle = value; QueueRedraw(); } }

        public override void _Draw()
        {
            // same shape as the browser game's SVG arrow (24x24 box), scaled to this control
            Vector2[] shape =
            {
                new Vector2(12, 1.5f), new Vector2(22, 12.5f), new Vector2(15.8f, 12.5f), new Vector2(15.8f, 22.5f),
                new Vector2(8.2f, 22.5f), new Vector2(8.2f, 12.5f), new Vector2(2, 12.5f),
            };
            float s = Mathf.Min(Size.X, Size.Y) / 24f;
            var c = Size / 2;
            var pts = new Vector2[shape.Length + 1];
            for (int i = 0; i < shape.Length; i++)
                pts[i] = c + ((shape[i] - new Vector2(12, 12)) * s).Rotated(angle);
            pts[shape.Length] = pts[0];
            // soft glow, fill, dark outline
            var glow = new Vector2[shape.Length];
            for (int i = 0; i < shape.Length; i++) glow[i] = c + ((shape[i] - new Vector2(12, 12)) * s * 1.18f).Rotated(angle);
            DrawColoredPolygon(glow, new Color(0.95f, 0.72f, 0.42f, 0.25f));
            var fill = new Vector2[shape.Length];
            System.Array.Copy(pts, fill, shape.Length);
            DrawColoredPolygon(fill, new Color("f2b76b"));
            DrawPolyline(pts, new Color("0b1414"), 1.6f, true);
        }
    }

    /// <summary>Centre dot with a short tick either side, like the browser reticle.</summary>
    public partial class Reticle : Control
    {
        public override void _Draw()
        {
            var c = Size / 2;
            var dark = new Color(.08f, .13f, .1f, .9f);
            var light = new Color("edf6e6");
            DrawRect(new Rect2(c - new Vector2(2.5f, 2.5f), new Vector2(5, 5)), dark);
            DrawRect(new Rect2(c - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), light);
            DrawRect(new Rect2(c + new Vector2(-14, -0.5f), new Vector2(10, 1)), new Color(light, .6f));
            DrawRect(new Rect2(c + new Vector2(4, -0.5f), new Vector2(10, 1)), new Color(light, .6f));
        }
    }
}

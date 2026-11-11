using Godot;

namespace BunkerBuster
{
    /// <summary>On-screen text: crosshair, level and objective readouts, and the centre overlay for briefings and results.</summary>
    public partial class Hud : CanvasLayer
    {
        Label topLeft, topRight, message, overlayTitle, overlayBody, overlayHint, kit, alertLabel;
        ColorRect hurtTint, alertFill, healthFill;
        Control alertBar;
        Control overlay;
        double messageTime;

        static Label MakeLabel(int size, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var l = new Label { HorizontalAlignment = align };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", new Color(.92f, .94f, .9f));
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .85f));
            l.AddThemeConstantOverride("outline_size", 6);
            return l;
        }

        public override void _Ready()
        {
            var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(root);

            var cross = new ColorRect { Color = new Color(1, 1, 1, .8f), Size = new Vector2(4, 4), MouseFilter = Control.MouseFilterEnum.Ignore };
            cross.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.KeepSize);
            root.AddChild(cross);

            topLeft = MakeLabel(20);
            topLeft.Position = new Vector2(18, 12);
            root.AddChild(topLeft);

            topRight = MakeLabel(20, HorizontalAlignment.Right);
            topRight.GrowHorizontal = Control.GrowDirection.Begin;
            topRight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight, Control.LayoutPresetMode.Minsize, 14);
            root.AddChild(topRight);

            message = MakeLabel(26, HorizontalAlignment.Center);
            message.GrowHorizontal = Control.GrowDirection.Both;
            message.GrowVertical = Control.GrowDirection.Begin;
            message.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 90);
            root.AddChild(message);

            hurtTint = new ColorRect { Color = new Color(.7f, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            hurtTint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(hurtTint);

            kit = MakeLabel(22, HorizontalAlignment.Right);
            kit.GrowHorizontal = Control.GrowDirection.Begin;
            kit.GrowVertical = Control.GrowDirection.Begin;
            kit.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 22);
            root.AddChild(kit);

            var healthBack = new ColorRect { Color = new Color(0, 0, 0, .55f), Size = new Vector2(260, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
            healthBack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.KeepSize, 28);
            root.AddChild(healthBack);
            healthFill = new ColorRect { Color = new Color(.85f, .9f, .82f), Size = new Vector2(260, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
            healthBack.AddChild(healthFill);

            alertBar = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            alertBar.CustomMinimumSize = new Vector2(300, 0);
            alertBar.Size = new Vector2(300, 34);
            alertBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.KeepSize, 14);
            alertLabel = MakeLabel(22, HorizontalAlignment.Center);
            alertBar.AddChild(alertLabel);
            var alertBack = new ColorRect { Color = new Color(0, 0, 0, .5f), CustomMinimumSize = new Vector2(300, 6), MouseFilter = Control.MouseFilterEnum.Ignore };
            alertFill = new ColorRect { Size = new Vector2(300, 6), MouseFilter = Control.MouseFilterEnum.Ignore };
            alertBack.AddChild(alertFill);
            alertBar.AddChild(alertBack);
            alertBar.Visible = false;
            root.AddChild(alertBar);

            overlay = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, .72f) };
            style.SetContentMarginAll(40);
            ((PanelContainer)overlay).AddThemeStyleboxOverride("panel", style);
            overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", 18);
            overlay.AddChild(box);
            overlayTitle = MakeLabel(54, HorizontalAlignment.Center);
            overlayBody = MakeLabel(22, HorizontalAlignment.Center);
            overlayBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            overlayBody.CustomMinimumSize = new Vector2(700, 0);
            overlayHint = MakeLabel(20, HorizontalAlignment.Center);
            overlayHint.AddThemeColorOverride("font_color", new Color(.55f, .9f, .6f));
            box.AddChild(overlayTitle);
            box.AddChild(overlayBody);
            box.AddChild(overlayHint);
            root.AddChild(overlay);
        }

        public void SetStatus(string left, string right)
        {
            topLeft.Text = left;
            topRight.Text = right;
        }

        static readonly Color[] AlertColors = { Colors.White, new Color(1, .82f, .25f), new Color(1, .55f, .2f), new Color(1, .25f, .2f) };

        /// <summary>Alert phase readout: hidden when undetected, otherwise name, seconds left and a draining bar.</summary>
        public void SetAlert(int level, string name, double timer, double fraction)
        {
            alertBar.Visible = level > 0;
            if (level == 0) return;
            alertLabel.Text = $"{name}  {timer:0.0}";
            alertLabel.AddThemeColorOverride("font_color", AlertColors[level]);
            alertFill.Color = AlertColors[level];
            alertFill.Size = new Vector2(300 * (float)Mathf.Clamp(fraction, 0, 1), 6);
        }

        public void SetKit(string text, int hp, double hurt)
        {
            kit.Text = text;
            healthFill.Size = new Vector2(260 * hp / 100f, 14);
            healthFill.Color = hp > 50 ? new Color(.85f, .9f, .82f) : hp > 25 ? new Color(1, .75f, .3f) : new Color(1, .3f, .25f);
            hurtTint.Color = new Color(.7f, 0, 0, (float)Mathf.Clamp(hurt * .7, 0, .45));
        }

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
            if (messageTime > 0)
            {
                messageTime -= delta;
                if (messageTime <= 0) message.Text = "";
            }
        }
    }
}

using Godot;

namespace BunkerBuster
{
    /// <summary>On-screen text: crosshair, level and objective readouts, and the centre overlay for briefings and results.</summary>
    public partial class Hud : CanvasLayer
    {
        Label topLeft, topRight, message, overlayTitle, overlayBody, overlayHint;
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
            cross.SetAnchorsPreset(Control.LayoutPreset.Center);
            cross.Position -= new Vector2(2, 2);
            root.AddChild(cross);

            topLeft = MakeLabel(20);
            topLeft.Position = new Vector2(18, 12);
            root.AddChild(topLeft);

            topRight = MakeLabel(20, HorizontalAlignment.Right);
            topRight.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            topRight.GrowHorizontal = Control.GrowDirection.Begin;
            topRight.Position += new Vector2(-18, 12);
            root.AddChild(topRight);

            message = MakeLabel(26, HorizontalAlignment.Center);
            message.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
            message.GrowHorizontal = Control.GrowDirection.Both;
            message.Position += new Vector2(0, -90);
            root.AddChild(message);

            overlay = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, .72f) };
            style.SetContentMarginAll(40);
            ((PanelContainer)overlay).AddThemeStyleboxOverride("panel", style);
            overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
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

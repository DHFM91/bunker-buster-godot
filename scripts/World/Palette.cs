using System;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// The per-deploy paint job (the colour half of JS rollPalette): concrete hue, floor, outer wall,
    /// reinforced walls, yard asphalt, trucks and drums, within believable ranges and nudged by the
    /// theme. The intel (orange) and the extraction pad (green) never change.
    /// The textures were exported with the browser game's default colours, so each material gets a
    /// tint = new colour / default colour, applied as the material's albedo colour.
    /// </summary>
    public sealed class Palette
    {
        /// <summary>The default colours the exported textures were drawn with (JS `colors` at start-up).</summary>
        static readonly int[][] Defaults =
        {
            new[] { 0, 0, 0 }, new[] { 96, 98, 94 }, new[] { 150, 148, 142 }, new[] { 96, 72, 58 },
            new[] { 43, 160, 106 }, new[] { 229, 143, 47 }, new[] { 86, 94, 62 }, new[] { 124, 121, 116 },
            new[] { 150, 132, 96 }, new[] { 86, 98, 60 }, new[] { 104, 110, 116 }, new[] { 62, 58, 54 },
            new[] { 58, 60, 60 }, new[] { 92, 98, 66 }, new[] { 34, 36, 38 }, new[] { 158, 34, 26 },
        };

        public readonly Color[] Tints = new Color[16];
        public Color Stripe;

        /// <summary>Optional theme paint ranges (JS theme.paint).</summary>
        public sealed class Paint
        {
            public double[] Hue, Sat, Light, Floor, Yard;
            public double? Accent;
        }

        public static Paint PaintFor(string themeId) => themeId switch
        {
            "arctic" => new Paint { Hue = new[] { 195.0, 220 }, Sat = new[] { .04, .1 }, Light = new[] { .6, .7 }, Floor = new[] { 205, .12, .8 }, Yard = new[] { 205, .1, .84 }, Accent = 0 },
            "desert" => new Paint { Hue = new[] { 28.0, 42 }, Sat = new[] { .14, .24 }, Light = new[] { .56, .66 }, Floor = new[] { 36, .22, .5 }, Yard = new[] { 38, .3, .58 } },
            "vault" => new Paint { Sat = new[] { .02, .05 }, Light = new[] { .44, .52 } },
            "lockdown" => new Paint { Accent = 0 },
            "overgrown" => new Paint { Hue = new[] { 70.0, 110 }, Sat = new[] { .05, .12 } },
            _ => new Paint(),
        };

        public static Palette Roll(Theme theme, Random rng)
        {
            double R(double a, double b) => a + rng.NextDouble() * (b - a);
            T Pick<T>(T[] a) => a[rng.Next(a.Length)];
            var T0 = PaintFor(theme.Id);
            double hue = T0.Hue != null ? R(T0.Hue[0], T0.Hue[1]) : R(0, 360);
            Color concrete = Mood.Hsl(hue, T0.Sat != null ? R(T0.Sat[0], T0.Sat[1]) : R(.03, .1), T0.Light != null ? R(T0.Light[0], T0.Light[1]) : R(.52, .62));
            Color floor = T0.Floor != null
                ? Mood.Hsl(T0.Floor[0] + R(-8, 8), T0.Floor[1] * R(.95, 1.05), T0.Floor[2] * R(.95, 1.05))
                : Mood.Hsl(hue + R(-25, 25), R(.03, .08), R(.38, .46));
            var o = Pick(new[] { new[] { 22, .42, .32 }, new[] { 28, .3, .3 }, new[] { 80, .16, .3 }, new[] { 205, .12, .34 }, new[] { 0, 0, .3 } });
            Color outer = Mood.Hsl(o[0] + R(-6, 6), o[1], o[2]);
            Color reinforced = Mood.Hsl(hue + R(160, 200), R(.03, .08), R(.42, .5));
            Color yard = T0.Yard != null
                ? Mood.Hsl(T0.Yard[0] + R(-8, 8), T0.Yard[1] * R(.95, 1.05), T0.Yard[2] * R(.95, 1.05))
                : Mood.Hsl(hue, R(.02, .05), R(.24, .3));
            var t = Pick(new[] { new[] { 75, .25, .3 }, new[] { 40, .3, .42 }, new[] { 200, .1, .36 }, new[] { 110, .2, .26 } });
            Color truck = Mood.Hsl(t[0] + R(-5, 5), t[1], t[2]);
            var d = Pick(new[] { new[] { 80, .3, .3 }, new[] { 210, .35, .32 }, new[] { 0, .45, .34 }, new[] { 45, .55, .4 } });
            double accentHue = (T0.Accent ?? Pick(new[] { 52.0, 50, 46, 0, 210, 280 })) + R(-4, 4);

            var p = new Palette { Stripe = Mood.Hsl(accentHue, R(.6, .8), R(.45, .55)) };
            Color[] colors =
            {
                Colors.Black, floor, concrete, outer, Def(4), Def(5), Def(6), concrete * .86f, Def(8),
                Mood.Hsl(d[0], d[1], d[2]), reinforced, Def(11), yard, truck, Def(14), Def(15),
            };
            if (theme.Moss)
            {
                // moss creeping over the concrete
                colors[2] = colors[2].Lerp(new Color(.36f, .45f, .26f), .28f);
                colors[10] = colors[10].Lerp(new Color(.36f, .45f, .26f), .22f);
            }
            for (int i = 0; i < 16; i++)
            {
                var def = Def(i);
                p.Tints[i] = i == 0 ? Colors.White : new Color(
                    def.R > 0 ? colors[i].R / def.R : 1, def.G > 0 ? colors[i].G / def.G : 1, def.B > 0 ? colors[i].B / def.B : 1);
            }
            if (theme.NightVision)
                for (int i = 1; i < 16; i++)
                {
                    // night-vision goggles: every surface a brightened grey; the green comes from the screen tint
                    var c = colors[i];
                    float l = Mathf.Min(1, (c.R * .3f + c.G * .59f + c.B * .11f) * 1.25f + .04f);
                    var def = Def(i);
                    p.Tints[i] = new Color(def.R > 0 ? l / def.R : 1, def.G > 0 ? l / def.G : 1, def.B > 0 ? l / def.B : 1);
                }
            return p;
        }

        static Color Def(int i) => new Color(Defaults[i][0] / 255f, Defaults[i][1] / 255f, Defaults[i][2] / 255f);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// First-person carbine viewmodel, drawn entirely with <see cref="CanvasItem"/> draw calls.
    /// A direct port of the browser game's drawRifle / drawPlant canvas code: the drawing works in a
    /// 180-unit-high design space (the JS game's low-res frame) and is scaled so that unit height maps
    /// onto this control's height, keeping aspect. The gun sits bottom-right.
    /// Set the public input fields every frame from the player/weapon state, call <see cref="OnShot"/>
    /// when a round is fired, and anchor the control full-rect. It never consumes input.
    /// </summary>
    public partial class ViewModel : Control
    {
        /// <summary>Seconds a plant takes (JS plantTime).</summary>
        public const float PlantTime = 0.55f;
        /// <summary>Total reload duration in seconds.</summary>
        public const float ReloadTime = 1.15f;
        /// <summary>Fire cooldown right after a shot.</summary>
        public const float ShotCooldown = 0.21f;
        /// <summary>Muzzle flash duration right after a shot.</summary>
        public const float FlashTime = 0.075f;

        /// <summary>Fire cooldown seconds left (0.21 right after a shot, 0 when ready).</summary>
        public float Cooldown;
        /// <summary>Reload seconds left (of 1.15), 0 when not reloading.</summary>
        public float Reload;
        /// <summary>Plant seconds left (of 0.55), 0 when not planting.</summary>
        public float PlantLeft;
        /// <summary>Muzzle flash timer seconds left (0.075 right after a shot).</summary>
        public float Flash;
        /// <summary>Walk bob amount, 0 standing still to 1 at full walk.</summary>
        public float Sway;
        /// <summary>Walk cycle phase in radians.</summary>
        public float StridePhase;
        /// <summary>Animation clock in seconds; advanced by _Process.</summary>
        public float Clock;

        /// <summary>True while a charge is being planted.</summary>
        public bool Planting => PlantLeft > 0f;

        sealed class Wisp
        {
            public float Born, X, Dx;
        }

        sealed class Grad
        {
            public float X0, Y0, X1, Y1;
            public Color[] Stops;
        }

        struct Xf
        {
            public float A, B, C, D, E, F;
            public static Xf Identity => new Xf { A = 1f, B = 0f, C = 0f, D = 1f, E = 0f, F = 0f };

            public void Translate(float tx, float ty)
            {
                E += A * tx + C * ty;
                F += B * tx + D * ty;
            }

            public void Scale(float sx, float sy)
            {
                A *= sx; B *= sx;
                C *= sy; D *= sy;
            }

            public void Rotate(float ang)
            {
                float cs = MathF.Cos(ang), sn = MathF.Sin(ang);
                float a = A * cs + C * sn, b = B * cs + D * sn;
                float c = -A * sn + C * cs, d = -B * sn + D * cs;
                A = a; B = b; C = c; D = d;
            }

            public Vector2 Apply(float x, float y) => new Vector2(A * x + C * y + E, B * x + D * y + F);
            public float UniformScale => MathF.Sqrt(A * A + B * B);
        }

        static readonly Dictionary<string, Color> CssCache = new Dictionary<string, Color>();
        readonly List<Wisp> _smoke = new List<Wisp>();
        Xf _m = Xf.Identity;

        /// <inheritdoc />
        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            ClipContents = true;
        }

        /// <inheritdoc />
        public override void _Process(double delta)
        {
            Clock += (float)delta;
            QueueRedraw();
        }

        /// <summary>Call when a round is fired: adds a muzzle smoke wisp.</summary>
        public void OnShot()
        {
            _smoke.Add(new Wisp { Born = Clock + 0.05f, X = 28f, Dx = (GD.Randf() - 0.5f) * 1.2f });
            if (_smoke.Count > 8) _smoke.RemoveAt(0);
        }

        /// <inheritdoc />
        public override void _Draw()
        {
            float s = Size.Y / 180f;
            if (s <= 0.0001f) return;
            float width = Size.X / s;   // frame width in design units (JS "width")
            DrawRifle(s, width, 180f);
            if (Planting) DrawPlant(s, width, 180f, 1f - PlantLeft / PlantTime);
        }

        // ---------- colour helpers ----------

        /// <summary>Parses "#rgb", "#rrggbb", "#rrggbbaa" and "rgba(r,g,b,a)" / "rgb(r,g,b)" CSS strings.</summary>
        static Color Css(string css)
        {
            if (CssCache.TryGetValue(css, out Color cached)) return cached;
            Color c;
            if (css[0] == '#')
            {
                string h = css.Substring(1);
                if (h.Length == 3) h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
                int r = Convert.ToInt32(h.Substring(0, 2), 16);
                int g = Convert.ToInt32(h.Substring(2, 2), 16);
                int b = Convert.ToInt32(h.Substring(4, 2), 16);
                int a = h.Length >= 8 ? Convert.ToInt32(h.Substring(6, 2), 16) : 255;
                c = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            }
            else
            {
                int open = css.IndexOf('(');
                int close = css.LastIndexOf(')');
                string[] parts = css.Substring(open + 1, close - open - 1).Split(',');
                float r = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                float g = float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                float b = float.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
                float a = parts.Length > 3 ? float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture) : 1f;
                c = new Color(r / 255f, g / 255f, b / 255f, a);
            }
            CssCache[css] = c;
            return c;
        }

        static Color Lerp(Color a, Color b, float t) => new Color(
            a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

        // ---------- gradient / primitive helpers (all take LOCAL design-space coordinates) ----------

        static Grad Solid(string css) => new Grad { Stops = new[] { Css(css) } };

        /// <summary>Linear gradient between two local points, stops evenly spaced (JS lg()).</summary>
        static Grad Lg(float x0, float y0, float x1, float y1, params string[] stops)
        {
            var cols = new Color[stops.Length];
            for (int i = 0; i < stops.Length; i++) cols[i] = Css(stops[i]);
            return new Grad { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Stops = cols };
        }

        /// <summary>Samples a gradient at a local point (clamped, like canvas gradients).</summary>
        static Color Sample(Grad g, float x, float y)
        {
            int n = g.Stops.Length;
            if (n == 1) return g.Stops[0];
            float dx = g.X1 - g.X0, dy = g.Y1 - g.Y0;
            float len2 = dx * dx + dy * dy;
            float t = len2 > 0f ? ((x - g.X0) * dx + (y - g.Y0) * dy) / len2 : 0f;
            t = Math.Clamp(t, 0f, 1f);
            float f = t * (n - 1);
            int i = Math.Min((int)f, n - 2);
            return Lerp(g.Stops[i], g.Stops[i + 1], f - i);
        }

        /// <summary>Filled polygon with a gradient/solid fill and optional 0.45-wide outline (JS poly()).</summary>
        void Poly(Grad fill, float[] p, string edge = null)
        {
            int n = p.Length / 2;
            var pts = new Vector2[n];
            var cols = new Color[n];
            for (int i = 0; i < n; i++)
            {
                pts[i] = _m.Apply(p[i * 2], p[i * 2 + 1]);
                cols[i] = Sample(fill, p[i * 2], p[i * 2 + 1]);
            }
            DrawPolygon(pts, cols);
            if (edge != null)
            {
                var loop = new Vector2[n + 1];
                Array.Copy(pts, loop, n);
                loop[n] = pts[0];
                DrawPolyline(loop, Css(edge), 0.45f * _m.UniformScale, true);
            }
        }

        void Poly(string fill, float[] p, string edge = null) => Poly(Solid(fill), p, edge);

        /// <summary>Round-capped polyline (JS line()). Caps are circles when the colour is opaque.</summary>
        void Line(string css, float w, float[] p)
        {
            Color c = Css(css);
            int n = p.Length / 2;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++) pts[i] = _m.Apply(p[i * 2], p[i * 2 + 1]);
            float sw = w * _m.UniformScale;
            DrawPolyline(pts, c, sw, true);
            if (c.A >= 0.999f)
                for (int i = 0; i < n; i++) DrawCircle(pts[i], sw * 0.5f, c);
        }

        /// <summary>
        /// Radial gradient disc approximated with ring quads coloured per vertex.
        /// offs are stop offsets (0..1, ascending, first 0), cols the matching colours.
        /// </summary>
        void RadialDisc(float cx, float cy, float r, float[] offs, Color[] cols)
        {
            const int Seg = 28;
            for (int i = 0; i < offs.Length - 1; i++)
            {
                float r0 = offs[i] * r, r1 = offs[i + 1] * r;
                for (int k = 0; k < Seg; k++)
                {
                    float a0 = k * MathF.Tau / Seg, a1 = (k + 1) * MathF.Tau / Seg;
                    Vector2 o1 = _m.Apply(cx + MathF.Cos(a0) * r1, cy + MathF.Sin(a0) * r1);
                    Vector2 o2 = _m.Apply(cx + MathF.Cos(a1) * r1, cy + MathF.Sin(a1) * r1);
                    if (r0 <= 0.0001f)
                    {
                        Vector2 ctr = _m.Apply(cx, cy);
                        DrawPolygon(new[] { ctr, o1, o2 }, new[] { cols[i], cols[i + 1], cols[i + 1] });
                    }
                    else
                    {
                        Vector2 i1 = _m.Apply(cx + MathF.Cos(a0) * r0, cy + MathF.Sin(a0) * r0);
                        Vector2 i2 = _m.Apply(cx + MathF.Cos(a1) * r0, cy + MathF.Sin(a1) * r0);
                        DrawPolygon(new[] { i1, o1, o2, i2 }, new[] { cols[i], cols[i + 1], cols[i + 1], cols[i] });
                    }
                }
            }
        }

        /// <summary>Point along the handguard/barrel axis: t=0 receiver front, t=1 muzzle.</summary>
        static void Axis(float t, out float x, out float y)
        {
            x = 30f - t * 2.6f;
            y = -17f - t * 29f;
        }

        // ---------- the carbine ----------

        void DrawRifle(float vs, float width, float height)
        {
            _smoke.RemoveAll(w => Clock - w.Born >= 1.2f);

            float recoil = Cooldown > 0.08f ? MathF.Sin((ShotCooldown - Cooldown) / 0.13f * MathF.PI) * 5f : 0f;
            float dip = Reload > 0f ? MathF.Sin(Reload / ReloadTime * MathF.PI) : 0f;
            float plant = PlantLeft > 0f ? MathF.Min(1f, (1f - PlantLeft / PlantTime) * 5f) : 0f;
            float idle = MathF.Sin(Clock * 2.2f) * 0.55f;

            _m = Xf.Identity;
            _m.Translate(
                vs * (width - 121.6f + MathF.Sin(StridePhase) * 4f * Sway + dip * 15f),
                vs * (height + 3f + MathF.Abs(MathF.Cos(StridePhase)) * 3f * Sway + idle + dip * 29f + plant * height * 0.34f + recoil));
            _m.Scale(vs * 320f / 224f, vs * 320f / 224f);
            _m.Rotate(-0.42f + dip * 0.27f - recoil * 0.014f);

            // right sleeve + stock heel (closest to camera)
            Poly(Lg(50, 10, 80, 40, "#4a5240", "#2c3226"), new float[] { 44, 44, 48, 18, 64, 8, 92, 14, 94, 44 });
            Line("#5e6750", 0.8f, new float[] { 50, 22, 66, 13, 88, 17 });
            Poly(Lg(52, 0, 72, 20, "#2a2f2d", "#121515"), new float[] { 50, 4, 62, -4, 76, 6, 70, 22, 56, 20 }, "#3d4442");

            // magazine (drops during reload)
            float lift = Reload > 0f && Reload < 0.82f && Reload > 0.22f ? MathF.Sin((Reload - 0.22f) / 0.6f * MathF.PI) : 0f;
            Xf saved = _m;
            _m.Translate(lift * 4f, lift * 26f);
            Poly(Lg(22, 14, 40, 40, "#3a403b", "#181c1b"), new float[] { 22, 12, 36, 10, 40, 24, 40, 40, 26, 44, 22, 28 }, "#4c5450");
            for (int k = 0; k < 4; k++)
                Line("#121615", 0.5f, new float[] { 24, 18 + k * 6, 38, 16 + k * 6.2f });
            _m = saved;

            // lower receiver + magwell
            Poly(Lg(14, -6, 50, 20, "#343a37", "#151918"), new float[] { 12, 8, 17, -6, 46, -8, 58, 4, 52, 16, 36, 12, 22, 16 }, "#4a524e");
            Poly("#202523", new float[] { 20, 6, 24, -2, 40, -3, 44, 6, 36, 10, 24, 12 });
            // upper receiver: side + lit top
            Poly(Lg(16, -16, 48, -4, "#5d6863", "#2c3431"), new float[] { 14, -6, 19, -15, 46, -13, 50, -4, 44, -6, 24, -7 });
            Poly(Lg(20, -22, 46, -12, "#9aa6a0", "#5a6560"), new float[] { 19, -15, 25, -21, 41, -19.5f, 46, -13 });
            Line("#c9d2cc", 0.35f, new float[] { 25, -21, 41, -19.5f });
            // charging handle + ejection port
            Poly("#1c2120", new float[] { 40, -12, 47, -11, 48, -8, 41, -8.5f });
            Poly("#0e1111", new float[] { 30, -12, 39, -11.5f, 38, -9, 30, -9.6f });
            // worn edges
            Line("rgba(196,204,198,.38)", 0.4f, new float[] { 17, -6.2f, 24, -7.2f, 44, -6.2f });
            Line("rgba(196,204,198,.3)", 0.35f, new float[] { 20, -15.2f, 25.2f, -21.2f });
            Line("rgba(196,204,198,.32)", 0.35f, new float[] { 22.2f, 10.5f, 25.5f, 27.5f });
            Line("rgba(196,204,198,.25)", 0.3f, new float[] { 13, 7.6f, 17.2f, -5.8f });
            float[][] nicks =
            {
                new float[] { 32, -6.6f, 35, -7 }, new float[] { 18, 2, 19.4f, -1 },
                new float[] { 43, 1, 46, 3.2f }, new float[] { 27, 24, 29, 24.4f },
            };
            foreach (float[] q in nicks) Line("rgba(170,178,172,.35)", 0.25f, q);

            // handguard: tapers toward the muzzle, top rail with slots
            Poly(Lg(19, 0, 37, 0, "#232a28", "#4a5652", "#2a3230"), new float[] { 20, -16, 25.5f, -38, 30.8f, -38.6f, 38, -17 }, "#56625d");
            for (int k = 0; k < 6; k++)
            {
                Axis(0.1f + k * 0.1f, out float ax, out float ay);
                float sc = 1f - k * 0.065f;
                Poly("#121615", new float[]
                {
                    ax - 2.2f * sc, ay, ax + 2.2f * sc, ay, ax + 2f * sc, ay - 1.1f * sc, ax - 2f * sc, ay - 1.1f * sc,
                });
            }
            for (int k = 0; k < 3; k++)
            {
                Axis(0.2f + k * 0.25f, out float ax, out float ay);
                Poly("#151a19", new float[]
                {
                    ax + 3.4f - k * 0.4f, ay + 1, ax + 4.6f - k * 0.5f, ay + 1,
                    ax + 4.2f - k * 0.5f, ay - 2.4f, ax + 3.1f - k * 0.4f, ay - 2.4f,
                });
            }

            // holographic sight (sits low on the receiver)
            saved = _m;
            _m.Translate(33, -20); _m.Scale(0.82f, 0.82f); _m.Translate(-33, 20); _m.Translate(0, 4);
            Poly("#1a1f1e", new float[] { 26, -19.5f, 39, -18, 40, -21.5f, 27, -23 });
            Poly("#0d1010", new float[] { 29, -19, 31, -18.8f, 31, -16.6f, 29, -16.8f });
            Poly("#0d1010", new float[] { 35, -18.4f, 37, -18.2f, 37, -16, 35, -16.2f });
            Poly(Lg(24, -34, 40, -20, "#2f3735", "#121615"),
                new float[] { 25, -21.5f, 24.8f, -31, 27.5f, -34.5f, 37.5f, -33.8f, 40.5f, -30, 40.5f, -20.5f }, "#59645f");
            Line("rgba(200,210,204,.35)", 0.35f, new float[] { 25.2f, -30.6f, 27.7f, -34.1f, 37.3f, -33.5f });
            Poly(Lg(27, -32, 38, -23, "rgba(80,150,160,.85)", "rgba(20,46,52,.9)", "rgba(60,110,120,.85)"),
                new float[] { 27.4f, -23.2f, 27.3f, -30, 29, -32.2f, 36.4f, -31.7f, 38.2f, -29.3f, 38.2f, -23 });
            Line("rgba(220,250,255,.55)", 0.6f, new float[] { 29.4f, -30.8f, 33.8f, -24.5f });
            RadialDisc(32.6f, -27.6f, 1.8f, new[] { 0f, 0.35f, 1f },
                new[] { Css("rgba(255,90,70,1)"), Css("rgba(255,40,30,.7)"), Css("rgba(255,40,30,0)") });
            Poly("#0f1312", new float[] { 39.5f, -28, 42, -27.5f, 42, -24, 39.6f, -24.3f });
            _m = saved;

            // right glove on the pistol grip + trigger finger
            Poly(Lg(36, 4, 58, 30, "#3c3f39", "#1d1f1b"), new float[] { 37, 6, 50, 2, 58, 12, 60, 28, 46, 34, 36, 22 }, "#575b52");
            for (int k = 0; k < 3; k++)
                Line("#5a5e55", 0.5f, new float[] { 40 + k * 3, 10 + k * 5, 50 + k * 2.5f, 8 + k * 5 });
            Poly("#33362f", new float[] { 29, 5, 38, 3.5f, 39, 7, 30, 8.5f });

            // muzzle smoke: a few grey wisps curl up from the barrel after each shot
            foreach (Wisp w in _smoke)
            {
                float age = Clock - w.Born;
                if (age < 0f || age > 1.1f) continue;
                float r = 3f + age * 14f, a = (1f - age / 1.1f) * 0.22f;
                float px = w.X + w.Dx * age * 12f, py = -44f - age * 22f;
                RadialDisc(px, py, r, new[] { 0f, 1f },
                    new[] { new Color(176 / 255f, 174 / 255f, 166 / 255f, a), new Color(176 / 255f, 174 / 255f, 166 / 255f, 0f) });
            }

            // muzzle flash: randomised star + bloom
            if (Flash > 0f)
            {
                float k = Flash / FlashTime;
                saved = _m;
                _m.Translate(28.2f, -41.5f);
                _m.Rotate(GD.Randf() * 6.28f);
                RadialDisc(0f, 0f, 16f, new[] { 0f, 0.25f, 1f }, new[]
                {
                    new Color(1f, 240 / 255f, 190 / 255f, 0.9f * k),
                    new Color(1f, 170 / 255f, 70 / 255f, 0.55f * k),
                    new Color(1f, 120 / 255f, 40 / 255f, 0f),
                });
                var star = new float[28];
                for (int n = 0; n < 14; n++)
                {
                    float ang = n / 14f * 6.283f;
                    float rr = n % 2 == 1 ? 2.2f : 5f + GD.Randf() * 5f;
                    star[n * 2] = MathF.Cos(ang) * rr;
                    star[n * 2 + 1] = MathF.Sin(ang) * rr * 1.3f;
                }
                Poly("#ffb24a", star);
                var core = new float[28];
                for (int i = 0; i < 28; i++) core[i] = star[i] * 0.45f;
                Poly("#fff6d0", core);
                _m = saved;
                // (JS also tints the gun with a source-atop warm glow; not reproducible per-pixel here.)
            }
        }

        // ---------- the charge being planted ----------

        void DrawPlant(float vs, float width, float height, float progress)
        {
            float rise = MathF.Sin(progress * MathF.PI / 2f);
            float x = vs * (width / 2f - 12f + progress * 10f);
            float y = vs * (height + 6f - rise * height * 0.49f);
            _m = Xf.Identity;
            _m.Translate(x, y);
            _m.Scale(vs, vs);

            Poly(Lg(-8, 8, 26, 60, "#4f5844", "#2b3125"), new float[] { -10, 10, 28, 8, 30, 80, -12, 80 });
            Poly(Lg(0, 0, 24, 14, "#e2cf9e", "#b8a272"), new float[] { 0, 0, 24, 0, 24, 14, 0, 14 }, "#8a7a52");
            Poly("#525c40", new float[] { 4, 0, 7, 0, 7, 14, 4, 14 });
            Poly("#525c40", new float[] { 16, 0, 19, 0, 19, 14, 16, 14 });
            Poly("#22272a", new float[] { 13, -3, 22, -3, 22, 5, 13, 5 }, "#4a5256");
            Poly(MathF.Sin(progress * 30f) > 0f ? "#ff4a2a" : "#5a1e16", new float[] { 15, -1.5f, 18, -1.5f, 18, 1, 15, 1 });
            Line("#c03a2a", 0.6f, new float[] { 14, -3, 10, -7, 6, -5 });
            Line("#3a5ac0", 0.6f, new float[] { 20, -3, 22, -8, 26, -7 });
            Poly(Lg(-6, 4, 12, 20, "#3c3f39", "#1d1f1b"), new float[] { -6, 4, 6, 2, 12, 10, 8, 20, -6, 18 });
        }
    }
}

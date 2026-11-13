using System.Collections.Generic;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// Procedural PBR texture sets for the voxel materials, generated once and cached.
    /// Every texture is 256 x 256 and covers 4 x 4 voxels (64 px per voxel), tiles seamlessly, and is
    /// deterministic (fixed seeds, no System.Random). Each set is an albedo (sRGB), a tangent-space
    /// normal map (OpenGL convention, green = up) built from a height field, and a roughness map
    /// (greyscale, R channel). The mean albedo of every material is rescaled to the colour the
    /// runtime tint expects, so tinting by (new colour / default colour) still lands on the new colour.
    /// </summary>
    public static class TextureForge
    {
        public const int Size = 256;
        const int N = Size;
        const int NN = N * N;

        static readonly Dictionary<int, (ImageTexture Albedo, ImageTexture Normal, ImageTexture Roughness)> cache =
            new Dictionary<int, (ImageTexture Albedo, ImageTexture Normal, ImageTexture Roughness)>();

        static readonly int[] AllIds = { 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        /// <summary>
        /// Albedo (sRGB colours), normal map (tangent space, OpenGL convention: green = up, as Godot expects),
        /// roughness (greyscale, R channel used). Ids: 1 floor, 2 concrete, 3 outer wall, 4 extraction pad,
        /// 6 crate, 7 rubble, 8 sandbag, 9 drum, 10 reinforced, 11 bars, 12 yard, 13 truck, 14 truck dark,
        /// 15 fuel drum. Unknown ids get the floor set. Results are cached.
        /// </summary>
        public static (ImageTexture Albedo, ImageTexture Normal, ImageTexture Roughness) Make(int material)
        {
            if (cache.TryGetValue(material, out var hit)) return hit;
            var made = ToTextures(BuildBaked(material));
            cache[material] = made;
            return made;
        }

        /// <summary>Optional: builds every material up front, using all CPU cores for the pixel work.</summary>
        public static void WarmAll()
        {
            var todo = new List<int>();
            foreach (int id in AllIds) if (!cache.ContainsKey(id)) todo.Add(id);
            var baked = new Baked[todo.Count];
            System.Threading.Tasks.Parallel.For(0, todo.Count, k => { baked[k] = BuildBaked(todo[k]); });
            for (int k = 0; k < todo.Count; k++) cache[todo[k]] = ToTextures(baked[k]);
        }

        // ------------------------------------------------------------------ plumbing

        sealed class Layer
        {
            public readonly float[] H = new float[NN];   // height 0..1
            public readonly float[] R = new float[NN];
            public readonly float[] G = new float[NN];
            public readonly float[] B = new float[NN];
            public readonly float[] Q = new float[NN];   // roughness
            public readonly float Ns;                    // normal strength
            public Layer(float ns) { Ns = ns; }
        }

        sealed class Baked
        {
            public byte[] Albedo, Normal, Rough;
        }

        sealed class Wor
        {
            public readonly float[] F1 = new float[NN];
            public readonly float[] F2 = new float[NN];
            public readonly float[] Id = new float[NN];
            public readonly float[] Dx = new float[NN];
            public readonly float[] Dy = new float[NN];
        }

        static Baked BuildBaked(int m)
        {
            Layer L;
            int tr, tg, tb;
            switch (m)
            {
                case 2: L = Concrete(); tr = 150; tg = 148; tb = 142; break;
                case 3: L = OuterWall(); tr = 96; tg = 72; tb = 58; break;
                case 4: L = Pad(); tr = 43; tg = 160; tb = 106; break;
                case 6: L = Crate(); tr = 86; tg = 94; tb = 62; break;
                case 7: L = Rubble(); tr = 124; tg = 121; tb = 116; break;
                case 8: L = Sandbag(); tr = 150; tg = 132; tb = 96; break;
                case 9: L = Drum(false); tr = 86; tg = 98; tb = 60; break;
                case 10: L = Reinforced(); tr = 104; tg = 110; tb = 116; break;
                case 11: L = Bars(); tr = 62; tg = 58; tb = 54; break;
                case 12: L = Yard(); tr = 58; tg = 60; tb = 60; break;
                case 13: L = Truck(); tr = 92; tg = 98; tb = 66; break;
                case 14: L = TruckDark(); tr = 34; tg = 36; tb = 38; break;
                case 15: L = Drum(true); tr = 158; tg = 34; tb = 26; break;
                default: L = Floor(); tr = 96; tg = 98; tb = 94; break;
            }
            return Bake(L, tr, tg, tb);
        }

        static (ImageTexture Albedo, ImageTexture Normal, ImageTexture Roughness) ToTextures(Baked b)
        {
            return (MakeTex(b.Albedo), MakeTex(b.Normal), MakeTex(b.Rough));
        }

        static ImageTexture MakeTex(byte[] data)
        {
            Image img = Image.CreateFromData(N, N, false, Image.Format.Rgb8, data);
            img.GenerateMipmaps();
            return ImageTexture.CreateFromImage(img);
        }

        /// <summary>Rescales the mean colour to the target, derives the normal map, quantises to bytes.</summary>
        static Baked Bake(Layer L, int tr, int tg, int tb)
        {
            float[][] ch = { L.R, L.G, L.B };
            int[] tgt = { tr, tg, tb };
            for (int c = 0; c < 3; c++)
            {
                float[] a = ch[c];
                for (int i = 0; i < NN; i++) a[i] = Mathf.Clamp(a[i], 0.04f, 0.92f);
            }
            for (int it = 0; it < 4; it++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float[] a = ch[c];
                    double sum = 0.0;
                    for (int i = 0; i < NN; i++) sum += a[i];
                    float mean = (float)(sum / NN);
                    float k = (tgt[c] / 255f) / mean;
                    for (int i = 0; i < NN; i++) a[i] = Mathf.Clamp(a[i] * k, 0.04f, 0.92f);
                }
            }

            var res = new Baked { Albedo = new byte[NN * 3], Normal = new byte[NN * 3], Rough = new byte[NN * 3] };
            float[] h = L.H;
            float ns = L.Ns;
            for (int y = 0; y < N; y++)
            {
                int ym = (y + N - 1) & (N - 1), yp = (y + 1) & (N - 1);
                for (int x = 0; x < N; x++)
                {
                    int xm = (x + N - 1) & (N - 1), xp = (x + 1) & (N - 1);
                    int i = y * N + x;
                    float dx = (h[y * N + xp] - h[y * N + xm]) * 0.5f * ns;
                    float dy = (h[yp * N + x] - h[ym * N + x]) * 0.5f * ns;
                    float nx = -dx, ny = dy, nz = 1f;
                    float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    int o = i * 3;
                    res.Albedo[o] = Q8(L.R[i]);
                    res.Albedo[o + 1] = Q8(L.G[i]);
                    res.Albedo[o + 2] = Q8(L.B[i]);
                    res.Normal[o] = Q8(nx * inv * 0.5f + 0.5f);
                    res.Normal[o + 1] = Q8(ny * inv * 0.5f + 0.5f);
                    res.Normal[o + 2] = Q8(nz * inv * 0.5f + 0.5f);
                    byte q = Q8(Mathf.Clamp(L.Q[i], 0.05f, 1f));
                    res.Rough[o] = q;
                    res.Rough[o + 1] = q;
                    res.Rough[o + 2] = q;
                }
            }
            return res;
        }

        static byte Q8(float v)
        {
            return (byte)Mathf.Clamp((int)(v * 255f + 0.5f), 0, 255);
        }

        // ------------------------------------------------------------------ scalar helpers

        static float Cl01(float x) { return x < 0f ? 0f : (x > 1f ? 1f : x); }
        static float Sm(float a, float b, float x) { float t = Cl01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float Lp(float a, float b, float t) { return a + (b - a) * t; }
        static float Fr(float x) { return x - Mathf.Floor(x); }
        static float Md(float x, float m) { return x - m * Mathf.Floor(x / m); }
        static float Ab(float x) { return x < 0f ? -x : x; }
        static float Mx(float a, float b) { return a > b ? a : b; }
        static float Mn(float a, float b) { return a < b ? a : b; }
        static float St(float e, float x) { return x >= e ? 1f : 0f; }
        static float Hy(float a, float b) { return Mathf.Sqrt(a * a + b * b); }
        static int Wrap(int v) { return ((v % N) + N) % N; }

        /// <summary>Integer hash to [0,1).</summary>
        static float H01(int i, int j, int s)
        {
            unchecked
            {
                uint h = (uint)i * 374761393u + (uint)j * 668265263u + (uint)s * 1274126177u;
                h ^= h >> 15; h *= 2246822519u;
                h ^= h >> 13; h *= 3266489917u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        // ------------------------------------------------------------------ field generators (whole-tile arrays)

        /// <summary>Periodic value noise on an fx x fy lattice, 0..1.</summary>
        static float[] VNoise(int seed, int fx, int fy)
        {
            var tbl = new float[fx * fy];
            for (int j = 0; j < fy; j++)
                for (int i = 0; i < fx; i++) tbl[j * fx + i] = H01(i, j, seed);
            var i0 = new int[N]; var i1 = new int[N]; var sx = new float[N];
            var j0 = new int[N]; var j1 = new int[N]; var sy = new float[N];
            for (int k = 0; k < N; k++)
            {
                float px = (k + 0.5f) / N * fx;
                int a = (int)Mathf.Floor(px); float t = px - a;
                i0[k] = a % fx; i1[k] = (a + 1) % fx; sx[k] = t * t * (3f - 2f * t);
                float py = (k + 0.5f) / N * fy;
                int b = (int)Mathf.Floor(py); float u = py - b;
                j0[k] = b % fy; j1[k] = (b + 1) % fy; sy[k] = u * u * (3f - 2f * u);
            }
            var o = new float[NN];
            for (int y = 0; y < N; y++)
            {
                int r0 = j0[y] * fx, r1 = j1[y] * fx;
                float wy = sy[y];
                for (int x = 0; x < N; x++)
                {
                    float wx = sx[x];
                    float a = tbl[r0 + i0[x]], b = tbl[r0 + i1[x]], c = tbl[r1 + i0[x]], d = tbl[r1 + i1[x]];
                    float t0 = a + (b - a) * wx;
                    float t1 = c + (d - c) * wx;
                    o[y * N + x] = t0 + (t1 - t0) * wy;
                }
            }
            return o;
        }

        /// <summary>Fractal sum of VNoise octaves (lattice sizes (f&lt;&lt;k)+k so octaves don't line up), normalised to 0..1.</summary>
        static float[] Fbm(int seed, int fx, int fy, int oct)
        {
            var o = new float[NN];
            float tot = 0f, amp = 1f;
            for (int k = 0; k < oct; k++)
            {
                int a = (fx << k) + k, b = (fy << k) + k;
                if (a > N || b > N) break;
                float[] v = VNoise(seed + k * 37, a, b);
                for (int i = 0; i < NN; i++) o[i] += amp * v[i];
                tot += amp; amp *= 0.5f;
            }
            for (int i = 0; i < NN; i++) o[i] /= tot;
            return o;
        }

        static float[] White(int seed)
        {
            var o = new float[NN];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++) o[y * N + x] = H01(x, y, seed);
            return o;
        }

        /// <summary>Periodic cellular noise (distances in cell units; Id is a per-cell random 0..1; Dx/Dy point from the pixel to its feature point).</summary>
        static Wor Worley(int seed, int cells)
        {
            var ptx = new float[cells * cells]; var pty = new float[cells * cells]; var idt = new float[cells * cells];
            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                {
                    int k = j * cells + i;
                    ptx[k] = 0.1f + 0.8f * H01(i, j, seed);
                    pty[k] = 0.1f + 0.8f * H01(i, j, seed + 7);
                    idt[k] = H01(i, j, seed + 13);
                }
            var w = new Wor();
            float inv = (float)cells / N;
            for (int y = 0; y < N; y++)
            {
                float py = (y + 0.5f) * inv;
                int cj = (int)Mathf.Floor(py);
                for (int x = 0; x < N; x++)
                {
                    float px = (x + 0.5f) * inv;
                    int ci = (int)Mathf.Floor(px);
                    float f1 = 1e9f, f2 = 1e9f, wid = 0f, wdx = 0f, wdy = 0f;
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int nj = cj + dj;
                        int wj = ((nj % cells) + cells) % cells;
                        for (int di = -1; di <= 1; di++)
                        {
                            int ni = ci + di;
                            int wi = ((ni % cells) + cells) % cells;
                            int k = wj * cells + wi;
                            float ddx = ni + ptx[k] - px;
                            float ddy = nj + pty[k] - py;
                            float d = ddx * ddx + ddy * ddy;
                            if (d < f1) { f2 = f1; f1 = d; wid = idt[k]; wdx = ddx; wdy = ddy; }
                            else if (d < f2) { f2 = d; }
                        }
                    }
                    int p = y * N + x;
                    w.F1[p] = Mathf.Sqrt(f1); w.F2[p] = Mathf.Sqrt(f2); w.Id[p] = wid; w.Dx[p] = wdx; w.Dy[p] = wdy;
                }
            }
            return w;
        }

        /// <summary>Draws wrapped hairline scratches into dst (keeps the max).</summary>
        static void Scratch(float[] dst, int seed, int count, float lmin, float lmax, float ang, float spread, float amp)
        {
            for (int k = 0; k < count; k++)
            {
                float x0 = H01(k, 1, seed) * N;
                float y0 = H01(k, 2, seed) * N;
                float a = ang + (H01(k, 3, seed) - 0.5f) * spread;
                float len = lmin + (lmax - lmin) * H01(k, 4, seed);
                float ia = amp * (0.4f + 0.6f * H01(k, 5, seed));
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                int steps = (int)(len * 2f);
                bool horiz = Mathf.Abs(ca) > Mathf.Abs(sa);
                for (int s = 0; s < steps; s++)
                {
                    float t = (float)s / steps;
                    float f = Mathf.Min(1f, Mathf.Min(t, 1f - t) * 6f);
                    float x = x0 + ca * 0.5f * s;
                    float y = y0 + sa * 0.5f * s;
                    int xi = Wrap((int)Mathf.Floor(x)), yi = Wrap((int)Mathf.Floor(y));
                    float v = ia * f;
                    int p = yi * N + xi;
                    if (v > dst[p]) dst[p] = v;
                    int xj = xi, yj = yi;
                    if (horiz) yj = (yi + 1) % N; else xj = (xi + 1) % N;
                    int q = yj * N + xj;
                    if (v * 0.45f > dst[q]) dst[q] = v * 0.45f;
                }
            }
        }

        /// <summary>Ragged hairline cracks along cell borders, only where a slow mask noise is high (0..1).</summary>
        static float[] Cracks(int seed, int cells, int mseed, float mlo, float mhi, float w)
        {
            var wk = Worley(seed, cells);
            float[] rag = Fbm(seed + 5, 16, 16, 3);
            float[] mk = Fbm(mseed, 3, 3, 2);
            var o = new float[NN];
            for (int i = 0; i < NN; i++)
            {
                float e = (wk.F2[i] - wk.F1[i]) + (rag[i] - 0.5f) * 0.12f;
                o[i] = (1f - Sm(w * 0.5f, w * 1.6f, e)) * Sm(mlo, mhi, mk[i]);
            }
            return o;
        }

        // ------------------------------------------------------------------ 1 floor: poured slab

        static Layer Floor()
        {
            var L = new Layer(4f);
            float[] mott = Fbm(11, 3, 3, 4), mid = Fbm(12, 10, 10, 4), wn = White(13);
            float[] ta = Fbm(14, 2, 40, 2), tb = Fbm(15, 40, 2, 2), tmN = Fbm(16, 3, 3, 2);
            Wor wk = Worley(17, 64);
            float[] stN = Fbm(18, 2, 2, 4), st2N = Fbm(19, 5, 5, 3);
            float[] cr = Cracks(20, 4, 21, 0.62f, 0.72f, 0.02f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    float trow = Lp(ta[i], tb[i], Sm(0.4f, 0.6f, tmN[i]));
                    float dj = Mn(Mn(fx, N - fx), Mn(fy, N - fy));
                    float groove = 1f - Sm(1f, 3f, dj);
                    float lip = Sm(2.5f, 4f, dj) * (1f - Sm(4f, 7f, dj));
                    float wf = 1f - Sm(0.12f, 0.34f, wk.F1[i]);
                    float pit = St(0.8f, wk.Id[i]) * wf;
                    float chip = St(wk.Id[i], 0.12f) * wf;
                    float st = Sm(0.54f, 0.7f, stN[i]);
                    float st2 = Sm(0.58f, 0.72f, st2N[i]);
                    L.H[i] = 0.6f + 0.9f * (trow - 0.5f) * 0.25f + 0.5f * (mid[i] - 0.5f) * 0.3f - 0.55f * groove - 0.18f * pit + 0.06f * lip + 0.06f * (wn[i] - 0.5f) - 0.2f * cr[i];
                    float sh = 1f + 0.7f * (mott[i] - 0.5f) + 0.55f * (mid[i] - 0.5f) + 0.45f * (trow - 0.5f) - 0.12f * st - 0.08f * st2 - 0.5f * groove - 0.2f * pit + 0.18f * chip + 0.12f * (wn[i] - 0.5f) + 0.05f * lip - 0.3f * cr[i];
                    L.R[i] = 0.385f * sh * (1f + 0.04f * st);
                    L.G[i] = 0.395f * sh;
                    L.B[i] = 0.375f * sh * (1f - 0.04f * st);
                    L.Q[i] = 0.88f + 0.12f * (0.5f - trow) + 0.07f * groove - 0.09f * st + 0.04f * (wn[i] - 0.5f);
                }
            return L;
        }

        // ------------------------------------------------------------------ 2 concrete: board-formed panels

        static Layer Concrete()
        {
            var L = new Layer(5f);
            float[] grain = Fbm(22, 3, 56, 3), grain2 = Fbm(23, 5, 20, 3);
            float[] wsN = Fbm(24, 30, 3, 3);
            Wor wk = Worley(25, 64);
            float[] wn = White(26);
            float[] cr = Cracks(27, 5, 28, 0.58f, 0.68f, 0.022f);
            float[] mott = Fbm(29, 3, 3, 4), blot = Fbm(30, 9, 9, 4);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    float ptone = H01(y / 128, 0, 21) - 0.5f;
                    float dxs = Mn(fx, N - fx);
                    float dys = Ab(Md(fy + 64f, 128f) - 64f);
                    float seam = 1f - Sm(0.8f, 2.4f, Mn(dxs, dys));
                    float db = Ab(Md(fy + 16f, 32f) - 16f);
                    float bline = (1f - Sm(0.4f, 1.4f, db)) * 0.3f;
                    float ddx = Md(fx, 128f) - 64f, ddy = Md(fy, 64f) - 32f;
                    float dh = Hy(ddx, ddy);
                    float hole = 1f - Sm(3.6f, 5f, dh);
                    float rim = Sm(3.6f, 5f, dh) * (1f - Sm(5.5f, 8f, dh));
                    float dys2 = Md(fy - 32f, 64f);
                    float tw = Ab(ddx) + (grain2[i] - 0.5f) * 5f;
                    float tstreak = Sm(5f, 9f, dys2) * Mathf.Exp(-dys2 / 26f) * (1f - Sm(1.5f, 4.5f, tw));
                    float ws = Sm(0.5f, 0.72f, wsN[i]);
                    float wf = 1f - Sm(0.15f, 0.4f, wk.F1[i]);
                    float dk = St(0.72f, wk.Id[i]) * wf;
                    float lt = St(wk.Id[i], 0.22f) * wf;
                    L.H[i] = 0.7f + 0.7f * (grain[i] - 0.5f) * 0.12f + 0.2f * (blot[i] - 0.5f) * 0.3f - 0.45f * seam - 0.12f * bline - 0.9f * hole + 0.1f * rim - 0.1f * dk + 0.06f * lt + 0.04f * (wn[i] - 0.5f) - 0.5f * cr[i] + 0.05f * ptone * (1f - seam);
                    float sh = 1f + 0.1f * ptone * 2f + 0.5f * (mott[i] - 0.5f) + 0.45f * (blot[i] - 0.5f) + 0.35f * (grain[i] - 0.5f) - 0.08f * ws - 0.2f * tstreak - 0.4f * seam - 0.1f * bline - 0.5f * hole + 0.05f * rim - 0.15f * dk + 0.12f * lt + 0.1f * (wn[i] - 0.5f) - 0.3f * cr[i];
                    L.R[i] = 0.59f * sh * (1f + 0.05f * tstreak);
                    L.G[i] = 0.58f * sh;
                    L.B[i] = 0.555f * sh * (1f - 0.04f * tstreak);
                    L.Q[i] = 0.9f + 0.06f * (0.5f - grain[i]) + 0.05f * seam + 0.03f * tstreak - 0.04f * ws;
                }
            return L;
        }

        // ------------------------------------------------------------------ 3 outer wall: weathered brick

        static Layer OuterWall()
        {
            var L = new Layer(6f);
            float[] warp = Fbm(31, 16, 16, 3);
            float[] grn = Fbm(35, 24, 24, 4), wn = White(36);
            Wor wk = Worley(37, 64);
            float[] sootN = Fbm(38, 22, 2, 3), weathN = Fbm(39, 5, 5, 4);
            float[] eff1 = Fbm(40, 7, 7, 4), eff2 = Fbm(41, 11, 11, 2);
            float[] chipN = Fbm(42, 20, 20, 3);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    int row = y / 32;
                    float bxf = fx + (row % 2) * 32;
                    int col = ((int)Mathf.Floor(bxf / 64f)) % 4;
                    float lx = Md(bxf, 64f), ly = Md(fy, 32f);
                    float dEdge = Mn(Mn(lx, 64f - lx), Mn(ly, 32f - ly));
                    float de = dEdge + (warp[i] - 0.5f) * 6f;
                    float brick = Sm(1.2f, 3.6f, de);
                    float bevel = Sm(1.2f, 7f, de);
                    float bt = H01(row, col, 32), bh = H01(row, col, 33) - 0.5f, bu = H01(row, col, 34) - 0.5f;
                    float wf = 1f - Sm(0.15f, 0.4f, wk.F1[i]);
                    float spk = St(0.7f, wk.Id[i]) * wf;
                    float lsp = St(wk.Id[i], 0.2f) * wf;
                    float soot = Sm(0.45f, 0.7f, sootN[i]);
                    float weath = Sm(0.5f, 0.68f, weathN[i]);
                    float eff = Sm(0.6f, 0.72f, eff1[i]) * Sm(0.45f, 0.7f, eff2[i]);
                    float chip = Sm(0.55f, 0.7f, chipN[i]) * (1f - Sm(2f, 7f, de));
                    L.H[i] = 0.15f + 0.62f * brick + 0.1f * bevel + 0.07f * bh + 0.22f * (grn[i] - 0.5f) + 0.05f * (wn[i] - 0.5f) - 0.12f * spk - 0.1f * chip;
                    float shb = 1f + 0.3f * (bt - 0.5f) * 2f * 0.7f + 0.6f * (grn[i] - 0.5f) - 0.12f * soot - 0.1f * weath + 0.12f * (wn[i] - 0.5f) - 0.25f * spk + 0.18f * lsp - 0.08f * (1f - bevel) + 0.2f * eff + 0.12f * chip;
                    float br = 0.4f * (1f + 0.2f * bu * 2f), bg = 0.3f, bb = 0.235f * (1f - 0.15f * bu * 2f);
                    float mshade = 1f + 0.9f * (grn[i] - 0.5f) + 0.2f * (wn[i] - 0.5f) - 0.2f * weath;
                    L.R[i] = Lp(0.42f * mshade, br * shb, brick);
                    L.G[i] = Lp(0.395f * mshade, bg * shb, brick);
                    L.B[i] = Lp(0.36f * mshade, bb * shb, brick);
                    L.Q[i] = 0.9f + 0.07f * (grn[i] - 0.5f) - 0.04f * brick + 0.05f * (1f - brick) - 0.05f * eff;
                }
            return L;
        }

        // ------------------------------------------------------------------ 4 extraction pad: tread plate with hazard border

        static Layer Pad()
        {
            var L = new Layer(7f);
            float[] wornN = Fbm(43, 14, 14, 4);
            float[] mott = Fbm(44, 4, 4, 4), wn = White(45), fine = Fbm(46, 40, 40, 2);
            var sc = new float[NN]; Scratch(sc, 47, 60, 20f, 70f, 0.4f, 6.28f, 0.9f);
            float[] grimeN = Fbm(48, 8, 8, 4);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    float s = (((x / 32 + y / 32) & 1) == 1) ? 1f : -1f;
                    float la = Md(fx, 32f) - 16f, lb = Md(fy, 32f) - 16f;
                    float along = (la + s * lb) * 0.70711f, across = (lb - s * la) * 0.70711f;
                    float dxl = Mx(Ab(along) - 7f, 0f);
                    float d = Mathf.Sqrt(dxl * dxl + across * across);
                    float lugh = Sm(0f, 1f, 1f - d / 4.6f);
                    float lugtop = 1f - Sm(2.4f, 4.4f, d);
                    float dE = Mn(Mn(fx, N - fx), Mn(fy, N - fy));
                    const float Wd = 11f;
                    float band = 1f - Sm(Wd - 0.7f, Wd + 0.7f, dE);
                    float sp = Fr((fx + fy) / 32f);
                    float yel = Sm(0f, 0.04f, sp) - Sm(0.5f, 0.54f, sp);
                    float line = Sm(Wd, Wd + 0.7f, dE) * (1f - Sm(Wd + 1.6f, Wd + 2.4f, dE));
                    float worn = Sm(0.68f, 0.78f, wornN[i] + 0.1f * lugtop + 0.1f * band);
                    float grime = (1f - lugh) * Sm(0.4f, 0.65f, grimeN[i]);
                    L.H[i] = 0.22f + 0.55f * lugh + band * (0.45f - 0.22f - 0.55f * lugh) - 0.05f * line + 0.03f * (fine[i] - 0.5f) - 0.06f * sc[i] - 0.05f * worn;
                    float sh = 1f + 0.4f * (mott[i] - 0.5f) + 0.1f * lugtop + 0.4f * (fine[i] - 0.5f) - 0.12f * grime + 0.08f * (wn[i] - 0.5f);
                    float dr = 0.17f * sh, dg = 0.62f * sh, dbl = 0.42f * sh;
                    float wf = 1f + 0.5f * (fine[i] - 0.5f);
                    float wr = 0.33f * wf, wg = 0.34f * wf, wb = 0.33f * wf;
                    float wk = Cl01(worn * 0.9f + sc[i] * 0.6f);
                    dr = Lp(dr, wr, wk); dg = Lp(dg, wg, wk); dbl = Lp(dbl, wb, wk);
                    float ysh = 1f + 0.5f * (fine[i] - 0.5f) - 0.2f * grime + 0.25f * (mott[i] - 0.5f);
                    float cr = Lp(0.07f, 0.9f, yel) * ysh, cg = Lp(0.07f, 0.72f, yel) * ysh, cb = Lp(0.07f, 0.1f, yel) * ysh;
                    cr = Lp(cr, wr, worn * 0.7f); cg = Lp(cg, wg, worn * 0.7f); cb = Lp(cb, wb, worn * 0.7f);
                    float k = 1f - 0.5f * line;
                    L.R[i] = Lp(dr, cr, band) * k;
                    L.G[i] = Lp(dg, cg, band) * k;
                    L.B[i] = Lp(dbl, cb, band) * k;
                    L.Q[i] = 0.55f + 0.17f * worn + 0.1f * (1f - lugtop) * 0.5f - 0.12f * sc[i] + 0.06f * (fine[i] - 0.5f) - 0.05f * lugh;
                }
            return L;
        }

        // ------------------------------------------------------------------ 6 crate: stencilled ammo crate faces, one per voxel

        static Layer Crate()
        {
            var L = new Layer(6f);
            float[] g1 = Fbm(63, 2, 48, 3), g2 = Fbm(64, 3, 100, 2);
            float[] wearN = Fbm(66, 14, 14, 4);
            float[] wn = White(67), mott = Fbm(68, 4, 4, 3);
            var sc = new float[NN]; Scratch(sc, 69, 30, 10f, 40f, 0f, 0.6f, 0.8f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    float cx = Md(fx, 64f), cy = Md(fy, 64f);
                    int ci = x / 64, cj = y / 64;
                    int pk = (y % 64) / 32;
                    int pid = ci * 8 + (cj * 2 + pk);
                    float py = Md(fy, 32f);
                    float dp = Mn(py, 32f - py);
                    float seam = 1f - Sm(0.6f, 2.2f, dp);
                    float ptone = H01(pid, 3, 61) - 0.5f;
                    int off = (int)Mathf.Floor(H01(pid, 5, 62) * N);
                    int gx = (x + off) % N;
                    float grain = g1[y * N + gx], grain2 = g2[y * N + gx];
                    float ex = Mn(cx, 64f - cx), ey = Mn(cy, 64f - cy);
                    float br = Mx((1f - Sm(17f, 18f, ex)) * (1f - Sm(5f, 6f, ey)), (1f - Sm(5f, 6f, ex)) * (1f - Sm(17f, 18f, ey)));
                    float brin = Mx((1f - Sm(15f, 16.5f, ex)) * (1f - Sm(3.5f, 4.5f, ey)), (1f - Sm(3.5f, 4.5f, ex)) * (1f - Sm(15f, 16.5f, ey)));
                    float riv = Mx(1f - Sm(1.2f, 2f, Hy(ex - 3.2f, ey - 3.2f)), Mx(1f - Sm(1.2f, 2f, Hy(ex - 12.5f, ey - 3.2f)), 1f - Sm(1.2f, 2f, Hy(ex - 3.2f, ey - 12.5f))));
                    float bandm = Sm(21f, 22.5f, cy) * (1f - Sm(41.5f, 43f, cy));
                    float gl = Md(cx - 12f, 7f);
                    int gk = (int)Mathf.Floor((cx - 12f) / 7f);
                    int bits = (int)Mathf.Floor(H01(gk, ci * 4 + cj, 65) * 32f);
                    float gy = cy - 27f;
                    float inx = St(12f, cx) * St(cx, 52f);
                    float iny = St(0f, gy) * St(gy, 11f);
                    float vb1 = St(0f, gl) * St(gl, 1.7f) * iny * (bits & 1);
                    float vb2 = St(3.3f, gl) * St(gl, 5f) * iny * ((bits >> 1) & 1);
                    float hb1 = St(0f, gl) * St(gl, 5f) * St(0f, gy) * St(gy, 1.7f) * ((bits >> 2) & 1);
                    float hb2 = St(0f, gl) * St(gl, 5f) * St(4.6f, gy) * St(gy, 6.3f) * ((bits >> 3) & 1);
                    float hb3 = St(0f, gl) * St(gl, 5f) * St(9.3f, gy) * St(gy, 11f) * ((bits >> 4) & 1);
                    float glyph = Mx(Mx(Mx(vb1, vb2), Mx(hb1, hb2)), hb3) * inx;
                    float wear = Sm(0.68f, 0.76f, wearN[i] + 0.18f * (1f - Sm(0f, 6f, dp)) + 0.12f * br);
                    L.H[i] = 0.5f + 0.06f * (grain - 0.5f) + 0.04f * ptone - 0.35f * seam + 0.45f * br + 0.1f * riv + 0.02f * glyph - 0.04f * wear + 0.02f * (wn[i] - 0.5f) - 0.05f * sc[i];
                    float sh = (1f + 0.5f * ptone + 0.5f * (mott[i] - 0.5f) + 0.5f * (grain - 0.5f) + 0.12f * (wn[i] - 0.5f)) * (1f - 0.55f * seam) * (1f - 0.22f * bandm);
                    float pr = 0.34f * sh, pg = 0.37f * sh, pb = 0.245f * sh;
                    float wdsh = 1f + 0.9f * (grain2 - 0.5f) + 0.4f * ptone;
                    float wr = 0.52f * wdsh, wg = 0.38f * wdsh, wb = 0.22f * wdsh;
                    float wm = Cl01(wear + sc[i] * 0.8f) * (1f - br);
                    float r = Lp(pr, wr, wm), g = Lp(pg, wg, wm), b = Lp(pb, wb, wm);
                    float gm = glyph * (1f - wm);
                    r = Lp(r, 0.7f, gm * 0.8f); g = Lp(g, 0.68f, gm * 0.8f); b = Lp(b, 0.5f, gm * 0.8f);
                    float stl = 0.22f * (1f + 0.5f * (grain - 0.5f) + 0.24f * (1f - brin));
                    float rv = Cl01(riv);
                    L.R[i] = Lp(r, stl * (1f + 0.5f * rv), br);
                    L.G[i] = Lp(g, stl * 1.02f * (1f + 0.5f * rv), br);
                    L.B[i] = Lp(b, stl * 0.98f * (1f + 0.5f * rv), br);
                    L.Q[i] = 0.72f + 0.1f * wm + 0.05f * (0.5f - grain) - 0.28f * br + 0.04f * seam - 0.08f * gm;
                }
            return L;
        }

        // ------------------------------------------------------------------ 7 rubble: broken concrete chunks with gaps

        static Layer Rubble()
        {
            var L = new Layer(7f);
            Wor wk = Worley(71, 7);
            float[] grn = Fbm(73, 16, 16, 4), wn = White(74);
            Wor wk2 = Worley(75, 64);
            float[] mott = Fbm(76, 5, 5, 4), dustN = Fbm(77, 6, 6, 4);
            float[] cr = Cracks(78, 9, 79, 0.45f, 0.6f, 0.03f);
            for (int i = 0; i < NN; i++)
            {
                float e = wk.F2[i] - wk.F1[i];
                float hole = St(0.9f, wk.Id[i]);
                float chunk = Sm(0.02f, 0.16f, e);
                int cid = (int)(wk.Id[i] * 255f);
                float tl = (wk.Dx[i] * (H01(cid, 1, 72) - 0.5f) + wk.Dy[i] * (H01(cid, 2, 72) - 0.5f)) * 0.9f;
                float bas = 0.5f + 0.25f * H01(cid, 3, 72);
                float bt = H01(cid, 4, 72) - 0.5f;
                float wf = 1f - Sm(0.15f, 0.4f, wk2.F1[i]);
                float spk = St(0.72f, wk2.Id[i]) * wf;
                float lsp = St(wk2.Id[i], 0.22f) * wf;
                float dust = Sm(0.45f, 0.7f, dustN[i]);
                float cm = chunk * (1f - hole);
                L.H[i] = Lp(0.05f - 0.04f * hole, bas + tl + 0.18f * (grn[i] - 0.5f) * 2f * 0.5f + 0.04f * (wn[i] - 0.5f) - 0.1f * spk - 0.1f * cr[i], cm);
                float sh = 1f + 0.3f * bt + 0.6f * (mott[i] - 0.5f) + 0.8f * (grn[i] - 0.5f) + 0.12f * (wn[i] - 0.5f) - 0.25f * spk + 0.15f * lsp + 0.1f * tl + 0.15f * dust - 0.4f * cr[i];
                float gapc = 0.3f * (1f + 0.8f * (grn[i] - 0.5f)) * (1f - 0.25f * hole);
                L.R[i] = Lp(gapc, 0.5f * sh, cm);
                L.G[i] = Lp(gapc * 0.98f, 0.49f * sh, cm);
                L.B[i] = Lp(gapc * 0.95f, 0.47f * sh, cm);
                L.Q[i] = 0.92f + 0.05f * (grn[i] - 0.5f) - 0.03f * chunk;
            }
            return L;
        }

        // ------------------------------------------------------------------ 8 sandbag: woven burlap bags in rows

        static Layer Sandbag()
        {
            var L = new Layer(6f);
            float[] wr1 = Fbm(83, 4, 24, 3), wr2 = Fbm(84, 24, 5, 3), wrm = Fbm(85, 3, 3, 2);
            float[] wn = White(86), mott = Fbm(87, 4, 4, 4);
            float[] dirtN = Fbm(88, 6, 6, 4), stainN = Fbm(89, 9, 9, 3), fz = Fbm(90, 60, 60, 2);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    int row = y / 32;
                    float bxf = fx + (row % 2) * 32;
                    int col = ((int)Mathf.Floor(bxf / 64f)) % 4;
                    float lx = Md(bxf, 64f) / 64f, ly = Md(fy, 32f) / 32f;
                    float pa = Mathf.Sin(Mathf.Pi * lx), pb = Mathf.Sin(Mathf.Pi * ly);
                    float pill = Mathf.Pow(Mx(pa, 0f), 0.55f) * Mathf.Pow(Mx(pb, 0f), 0.55f);
                    float pillh = Sm(0f, 1f, pill);
                    float seam = 1f - Sm(0.1f, 0.45f, pill);
                    float tx = Mathf.Sin(Mathf.Pi * ((x & 3) + 0.5f) / 4f), ty = Mathf.Sin(Mathf.Pi * ((y & 3) + 0.5f) / 4f);
                    float weave = (((x >> 2) + (y >> 2)) & 1) == 1 ? tx : ty;
                    float bt = H01(row, col, 81) - 0.5f, bs = H01(row, col, 82);
                    float wrink = Lp(wr1[i], wr2[i], Sm(0.4f, 0.6f, wrm[i]));
                    float dirt = Sm(0.55f, 0.72f, dirtN[i]);
                    float stain = Sm(0.6f, 0.75f, stainN[i]) * St(0.55f, bs);
                    L.H[i] = 0.1f + 0.7f * pillh + 0.07f * (weave - 0.8f) + 0.12f * (wrink - 0.5f) + 0.03f * (wn[i] - 0.5f);
                    float sh = 1f + 0.35f * bt + 0.5f * (mott[i] - 0.5f) + 0.5f * (wrink - 0.5f) + 0.35f * (weave - 0.8f) - 0.35f * seam - 0.15f * dirt - 0.2f * stain + 0.2f * (fz[i] - 0.5f) + 0.1f * (wn[i] - 0.5f) - 0.12f * (1f - pillh);
                    L.R[i] = 0.6f * sh * (1f + 0.04f * bt);
                    L.G[i] = 0.52f * sh;
                    L.B[i] = 0.38f * sh * (1f - 0.1f * bt);
                    L.Q[i] = 0.93f + 0.05f * (wn[i] - 0.5f) - 0.05f * pillh + 0.04f * seam;
                }
            return L;
        }

        // ------------------------------------------------------------------ 9 / 15 drums: painted steel, ribs, rust (fuel drum: red with hazard diamond)

        static Layer Drum(bool fuel)
        {
            var L = new Layer(5f);
            float[] streak = Fbm(171, 44, 2, 3), streak2 = Fbm(172, 12, 3, 3);
            float[] mott = Fbm(173, 4, 4, 4), wn = White(174);
            float[] rnN = Fbm(175, 9, 9, 5);
            Wor wk = Worley(176, 64);
            float[] rs2N = Fbm(177, 4, 4, 2);
            var sc = new float[NN]; Scratch(sc, 178, 40, 10f, 50f, 0f, 6.28f, 0.9f);
            float[] rpit = Fbm(179, 48, 48, 2);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    float cx = Md(fx, 64f), cy = Md(fy, 64f);
                    float dr = Mn(Ab(cy - 14f), Ab(cy - 50f));
                    float rib = Sm(0f, 1f, 1f - dr / 5f);
                    float crease = Sm(5f, 6f, dr) * (1f - Sm(7f, 9f, dr));
                    float weld = 1f - Sm(0.6f, 1.8f, Ab(cy - 32f));
                    float vseam = 1f - Sm(0.6f, 1.8f, Mn(fx, N - fx));
                    float ribprox = 1f - Sm(0f, 14f, dr);
                    float rn = rnN[i] + 0.12f * ribprox + 0.08f * (cy / 64f);
                    float rust = Sm(0.67f, 0.75f, rn);
                    float rs2 = St(0.7f, wk.Id[i]) * (1f - Sm(0.1f, 0.4f, wk.F1[i])) * Sm(0.45f, 0.6f, rs2N[i]);
                    float rustall = Cl01(rust + rs2);
                    float fill = 0f, ring = 0f, mark = 0f;
                    if (fuel)
                    {
                        float ddx = Ab(cx - 32f), ddy = Ab(cy - 32f);
                        float dd = ddx + ddy;
                        fill = 1f - Sm(10.5f, 11.5f, dd);
                        ring = Sm(10.5f, 11.5f, dd) * (1f - Sm(13.5f, 14.5f, dd));
                        float ex = 1f - Sm(1.4f, 2f, ddx);
                        float ebar = ex * St(23f, cy) * St(cy, 34.5f) * fill;
                        float edot = (1f - Sm(1.4f, 2.2f, Hy(ddx, cy - 38.5f))) * fill;
                        mark = Mx(ebar, edot);
                    }
                    L.H[i] = 0.4f + 0.45f * rib - 0.25f * crease + 0.05f * weld + 0.04f * vseam + 0.05f * (streak[i] - 0.5f) - 0.07f * rustall * rpit[i] - 0.04f * sc[i] + 0.03f * (wn[i] - 0.5f) + 0.04f * (fill + ring);
                    float sh = 1f + 0.7f * (streak[i] - 0.5f) + 0.2f * (streak2[i] - 0.5f) + 0.5f * (mott[i] - 0.5f) + 0.12f * (wn[i] - 0.5f) - 0.2f * crease + 0.05f * rib - 0.2f * weld;
                    float pr, pg, pb;
                    if (fuel) { pr = 0.62f; pg = 0.13f; pb = 0.1f; } else { pr = 0.34f; pg = 0.385f; pb = 0.235f; }
                    float r = pr * sh, g = pg * sh, b = pb * sh;
                    float rk = 1f + 0.8f * (rpit[i] - 0.5f);
                    r = Lp(r, 0.46f * rk, rustall * 0.9f); g = Lp(g, 0.21f * rk, rustall * 0.9f); b = Lp(b, 0.09f * rk, rustall * 0.9f);
                    float mt = sc[i] * (1f - rustall) * 0.6f;
                    r = Lp(r, 0.45f, mt); g = Lp(g, 0.44f, mt); b = Lp(b, 0.42f, mt);
                    if (fuel)
                    {
                        r = Lp(r, 0.93f, fill); g = Lp(g, 0.76f, fill); b = Lp(b, 0.12f, fill);
                        r = Lp(r, 0.06f, ring); g = Lp(g, 0.06f, ring); b = Lp(b, 0.06f, ring);
                        r = Lp(r, 0.06f, mark); g = Lp(g, 0.06f, mark); b = Lp(b, 0.06f, mark);
                    }
                    L.R[i] = r; L.G[i] = g; L.B[i] = b;
                    L.Q[i] = 0.55f + 0.35f * rustall + 0.1f * crease - 0.15f * sc[i] + 0.04f * (wn[i] - 0.5f) - 0.06f * rib + 0.1f * (0.5f - streak[i]) * 0.5f;
                }
            return L;
        }

        // ------------------------------------------------------------------ 10 reinforced concrete: bolt plates and straps

        static Layer Reinforced()
        {
            var L = new Layer(5f);
            Wor wk = Worley(181, 64);
            float[] wn = White(182);
            float[] mott = Fbm(183, 4, 4, 4), blot = Fbm(184, 12, 12, 4), stainN = Fbm(185, 3, 3, 4);
            var sc = new float[NN]; Scratch(sc, 186, 30, 8f, 30f, 0f, 6.28f, 0.8f);
            float[] rustN = Fbm(187, 20, 20, 3);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    float cx = Md(fx, 64f), cy = Md(fy, 64f);
                    float ax = Ab(cx - 32f), ay = Ab(cy - 32f);
                    float pl = (1f - Sm(12.3f, 13.3f, ax)) * (1f - Sm(12.3f, 13.3f, ay));
                    float pe = Mn(13f - ax, 13f - ay);
                    float plh = Sm(0f, 2.5f, pe) * pl;
                    float bd = Hy(ax - 8.5f, ay - 8.5f);
                    float bolt = 1f - Sm(2.6f, 3.4f, bd);
                    float boltrim = Sm(2f, 2.8f, bd) * (1f - Sm(3.2f, 4f, bd));
                    float strap = Mx(1f - Sm(1.3f, 2.4f, ax), 1f - Sm(1.3f, 2.4f, ay)) * (1f - pl);
                    float edgeL = Mx(1f - Sm(0.5f, 1.5f, Mn(cx, 64f - cx)), 1f - Sm(0.5f, 1.5f, Mn(cy, 64f - cy))) * 0.5f;
                    float wf = 1f - Sm(0.12f, 0.34f, wk.F1[i]);
                    float pit = St(0.78f, wk.Id[i]) * wf;
                    float lsp = St(wk.Id[i], 0.15f) * wf;
                    float stain = Sm(0.55f, 0.72f, stainN[i]);
                    float rustE = (1f - Sm(0f, 3f, pe)) * pl * Sm(0.5f, 0.7f, rustN[i]);
                    L.H[i] = 0.5f + 0.06f * (mott[i] - 0.5f) + 0.04f * (blot[i] - 0.5f) - 0.1f * pit + 0.03f * (wn[i] - 0.5f) + 0.35f * plh + 0.1f * bolt + 0.08f * strap - 0.1f * edgeL - 0.04f * sc[i] * pl;
                    float sh = 1f + 0.5f * (mott[i] - 0.5f) + 0.45f * (blot[i] - 0.5f) - 0.12f * stain - 0.25f * pit + 0.15f * lsp + 0.12f * (wn[i] - 0.5f) - 0.18f * edgeL;
                    float cr = 0.455f * sh, cg = 0.48f * sh, cb = 0.51f * sh;
                    float shs = 1f - 0.25f * (1f - Sm(0f, 3f, pe)) + 0.5f * (blot[i] - 0.5f) + 0.15f * (wn[i] - 0.5f);
                    float sr = 0.27f * shs, sg = 0.285f * shs, sb = 0.31f * shs;
                    sr = Lp(sr, 0.42f, rustE * 0.6f); sg = Lp(sg, 0.24f, rustE * 0.6f); sb = Lp(sb, 0.13f, rustE * 0.6f);
                    sr = Lp(sr, 0.5f, sc[i] * 0.5f); sg = Lp(sg, 0.5f, sc[i] * 0.5f); sb = Lp(sb, 0.52f, sc[i] * 0.5f);
                    float cr2 = Lp(cr, 0.3f * sh, strap * 0.7f), cg2 = Lp(cg, 0.31f * sh, strap * 0.7f), cb2 = Lp(cb, 0.33f * sh, strap * 0.7f);
                    float br = 1f + 0.7f * bolt - 0.4f * boltrim;
                    L.R[i] = Lp(cr2, sr, pl) * br;
                    L.G[i] = Lp(cg2, sg, pl) * br;
                    L.B[i] = Lp(cb2, sb, pl) * br;
                    L.Q[i] = 0.88f + 0.05f * (mott[i] - 0.5f) - 0.42f * pl + 0.2f * rustE - 0.1f * bolt - 0.15f * sc[i] * pl + 0.04f * pit;
                }
            return L;
        }

        // ------------------------------------------------------------------ 11 bars: dark scratched steel

        static Layer Bars()
        {
            var L = new Layer(3.5f);
            float[] wn = White(191), mott = Fbm(192, 5, 5, 4);
            float[] va = Fbm(193, 64, 2, 2), vb = Fbm(194, 40, 3, 2);
            var sc = new float[NN]; Scratch(sc, 195, 90, 15f, 90f, 1.5708f, 0.9f, 0.9f);
            var sc2 = new float[NN]; Scratch(sc2, 196, 50, 10f, 60f, 0f, 6.28f, 0.7f);
            float[] rustA = Fbm(197, 6, 6, 5), rustB = Fbm(198, 12, 12, 3);
            float[] pitN = Fbm(199, 64, 64, 1);
            for (int i = 0; i < NN; i++)
            {
                float rust = Sm(0.64f, 0.76f, rustA[i]) * Sm(0.45f, 0.65f, rustB[i]);
                float pit = pitN[i];
                L.H[i] = 0.5f + 0.05f * (va[i] - 0.5f) - 0.07f * sc[i] - 0.05f * sc2[i] + 0.02f * (wn[i] - 0.5f) - 0.03f * rust * pit;
                float sh = 1f + 0.9f * (mott[i] - 0.5f) + 1f * (va[i] - 0.5f) + 0.6f * (vb[i] - 0.5f) + 0.1f * (wn[i] - 0.5f);
                float r = 0.245f * sh, g = 0.23f * sh, b = 0.215f * sh;
                float sca = Cl01(sc[i] + sc2[i] * 0.8f);
                r = Lp(r, 0.42f, sca * 0.7f); g = Lp(g, 0.41f, sca * 0.7f); b = Lp(b, 0.4f, sca * 0.7f);
                float rk = rust * 0.8f * (0.6f + 0.8f * pit);
                L.R[i] = Lp(r, 0.38f, rk); L.G[i] = Lp(g, 0.19f, rk); L.B[i] = Lp(b, 0.1f, rk);
                L.Q[i] = 0.55f + 0.12f * (0.5f - va[i]) + 0.3f * rust - 0.2f * sca + 0.04f * (wn[i] - 0.5f);
            }
            return L;
        }

        // ------------------------------------------------------------------ 12 yard: asphalt with oil stains and a faded dashed line

        static Layer Yard()
        {
            var L = new Layer(5f);
            Wor wk = Worley(201, 96), wk2 = Worley(202, 48);
            float[] wn = White(203);
            float[] mott = Fbm(204, 4, 4, 4), mid = Fbm(205, 14, 14, 4);
            float[] oil1 = Fbm(206, 3, 3, 4), oil2 = Fbm(207, 7, 7, 3);
            float[] cr = Cracks(208, 3, 209, 0.6f, 0.7f, 0.02f);
            float[] edgeN = Fbm(210, 12, 12, 3), paintN = Fbm(211, 24, 24, 4);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    float ag = wk.Id[i], ag2 = wk2.Id[i];
                    float chipd = 1f - Sm(0.15f, 0.55f, wk.F1[i]);
                    float chip2 = 1f - Sm(0.2f, 0.7f, wk2.F1[i]);
                    float oilm = Mx(Sm(0.56f, 0.72f, oil1[i]), Sm(0.6f, 0.74f, oil2[i]) * 0.7f);
                    float ux = Ab(fx - 128f);
                    float dy = Md(fy, 128f);
                    float dash = Sm(0f, 3f, dy) * (1f - Sm(81f, 84f, dy));
                    float lm = (1f - Sm(4.2f, 5.2f, ux + (edgeN[i] - 0.5f) * 3f)) * dash;
                    float cover = lm * Sm(0.28f, 0.5f, paintN[i] + 0.12f - 0.3f * (St(0.6f, ag) * chipd));
                    L.H[i] = 0.5f + 0.3f * (ag - 0.5f) * chipd + 0.12f * (ag2 - 0.5f) * chip2 + 0.05f * (mid[i] - 0.5f) + 0.04f * (wn[i] - 0.5f) - 0.3f * cr[i] - 0.02f * oilm + 0.02f * cover;
                    float sh = 1f + 0.4f * (ag - 0.5f) * chipd + 0.35f * (ag2 - 0.5f) * chip2 + 0.9f * (mott[i] - 0.5f) + 0.8f * (mid[i] - 0.5f) + 0.25f * (wn[i] - 0.5f) - 0.28f * oilm - 0.5f * cr[i];
                    float r = 0.225f * sh, g = 0.235f * sh, b = 0.235f * sh;
                    r = r * (1f + 0.1f * oilm); b = b * (1f + 0.03f * oilm);
                    float pw = 1f + 0.5f * (wn[i] - 0.5f);
                    float pm = 0.7f + 0.3f * sh;
                    float pr = 0.62f * pw * pm, pg = 0.54f * pw * pm, pb = 0.2f * pw * pm;
                    L.R[i] = Lp(r, pr, cover * 0.8f);
                    L.G[i] = Lp(g, pg, cover * 0.8f);
                    L.B[i] = Lp(b, pb, cover * 0.8f);
                    L.Q[i] = 0.93f + 0.04f * (wn[i] - 0.5f) - 0.45f * oilm - 0.06f * cover + 0.04f * cr[i];
                }
            return L;
        }

        // ------------------------------------------------------------------ 13 truck: riveted painted sheet-metal panels

        static Layer Truck()
        {
            var L = new Layer(5f);
            float[] rip = Fbm(92, 3, 3, 3), mott = Fbm(93, 5, 5, 4), wn = White(94);
            float[] stk = Fbm(95, 30, 2, 3);
            float[] wearN = Fbm(96, 20, 20, 3);
            var sc = new float[NN]; Scratch(sc, 97, 70, 12f, 60f, 0f, 6.28f, 0.9f);
            float[] mudN = Fbm(98, 4, 4, 4);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    float ptone = H01(x / 128, y / 128, 91) - 0.5f;
                    float ux = Ab(Md(fx + 64f, 128f) - 64f), uy = Ab(Md(fy + 64f, 128f) - 64f);
                    float d = Mn(ux, uy);
                    float seam = 1f - Sm(0.8f, 2.2f, d);
                    float rv1 = 1f - Sm(1.4f, 2.2f, Hy(Ab(ux - 6f), Ab(Md(fy, 16f) - 8f)));
                    float rv2 = 1f - Sm(1.4f, 2.2f, Hy(Ab(uy - 6f), Ab(Md(fx, 16f) - 8f)));
                    float riv = Mx(rv1, rv2);
                    float wearE = (1f - Sm(1f, 6f, d)) * Sm(0.5f, 0.64f, wearN[i]);
                    float tx = Mathf.Sin(Mathf.Pi * ((x & 1) + 0.5f) / 2f), ty = Mathf.Sin(Mathf.Pi * ((y & 1) + 0.5f) / 2f);
                    float weave = (((x >> 1) + (y >> 1)) & 1) == 1 ? tx : ty;
                    float mud = Sm(0.55f, 0.72f, mudN[i]) * (1f - Sm(0f, 128f, Md(fy, 128f)));
                    L.H[i] = 0.5f + 0.12f * (rip[i] - 0.5f) + 0.04f * ptone - 0.4f * seam + 0.25f * riv + 0.02f * (weave - 0.8f) + 0.02f * (wn[i] - 0.5f) - 0.05f * sc[i] - 0.04f * wearE;
                    float sh = (1f + 0.14f * ptone + 0.6f * (mott[i] - 0.5f) + 0.7f * (rip[i] - 0.5f) + 0.7f * (stk[i] - 0.5f) + 0.12f * (wn[i] - 0.5f) + 0.06f * (weave - 0.8f)) * (1f - 0.5f * seam) * (1f - 0.25f * mud);
                    float r = 0.36f * sh, g = 0.385f * sh, b = 0.26f * sh;
                    float wm = Cl01(wearE * 0.9f + sc[i] * 0.6f);
                    float wv = 1f + 0.8f * (wn[i] - 0.5f);
                    r = Lp(r, 0.4f * wv, wm); g = Lp(g, 0.3f * wv, wm); b = Lp(b, 0.22f * wv, wm);
                    float rb = 1f + 0.3f * riv;
                    L.R[i] = r * rb; L.G[i] = g * rb; L.B[i] = b * rb;
                    L.Q[i] = 0.62f + 0.18f * wm + 0.06f * (wn[i] - 0.5f) - 0.08f * riv + 0.05f * mud + 0.04f * seam;
                }
            return L;
        }

        // ------------------------------------------------------------------ 14 truck dark: rubber / chassis

        static Layer TruckDark()
        {
            var L = new Layer(4f);
            float[] wn = White(101), fz = Fbm(102, 40, 40, 2), mott = Fbm(103, 4, 4, 4);
            float[] dustN = Fbm(104, 5, 5, 4);
            var sc = new float[NN]; Scratch(sc, 105, 50, 10f, 50f, 0f, 6.28f, 0.8f);
            Wor wk = Worley(106, 64);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float fx = x + 0.5f, fy = y + 0.5f;
                    float rib = 0.5f + 0.5f * Mathf.Cos(2f * Mathf.Pi * fy / 16f);
                    float dm = Mn(Ab(Md(fx + 64f, 128f) - 64f), Ab(Md(fy + 64f, 128f) - 64f));
                    float mold = 1f - Sm(0.8f, 2.2f, dm);
                    float dust = Sm(0.55f, 0.7f, dustN[i]);
                    float spk = St(0.75f, wk.Id[i]) * (1f - Sm(0.15f, 0.4f, wk.F1[i]));
                    L.H[i] = 0.5f + 0.12f * rib + 0.05f * (fz[i] - 0.5f) + 0.04f * (wn[i] - 0.5f) - 0.3f * mold - 0.05f * sc[i] - 0.05f * spk;
                    float sh = 1f + 0.9f * (mott[i] - 0.5f) + 0.8f * (fz[i] - 0.5f) + 0.25f * (wn[i] - 0.5f) + 0.1f * (rib - 0.5f) - 0.3f * mold + 0.5f * dust - 0.3f * spk;
                    float r = 0.13f * sh, g = 0.138f * sh, b = 0.15f * sh;
                    L.R[i] = Lp(r, 0.24f, sc[i] * 0.5f);
                    L.G[i] = Lp(g, 0.24f, sc[i] * 0.5f);
                    L.B[i] = Lp(b, 0.25f, sc[i] * 0.5f);
                    L.Q[i] = 0.82f + 0.1f * dust + 0.05f * (wn[i] - 0.5f) - 0.2f * sc[i] + 0.03f * mold;
                }
            return L;
        }
    }
}

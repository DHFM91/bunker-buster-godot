using System;
using System.Collections.Generic;
using Godot;

namespace BunkerBuster
{
    /// <summary>Where a ray hit: the voxel, the distance along the ray, which axis the face is on.</summary>
    public struct RayHit
    {
        public int X, Y, Z;
        public double T;
        public int Side;     // 0 x face, 1 y face, 2 z face
        public byte Material;
        public bool Hit => Material != 0;
    }

    /// <summary>
    /// Ray tracing against the voxels, carving holes, and structural collapse, ported from trace(),
    /// carve(), checkIntegrity() and updateStructure() in bunker-buster.js.
    /// Concrete (2) and rubble (7) need support: each such voxel must be within SPAN steps (sideways
    /// or down) of a column standing on an anchor. Anything left hanging cracks loose, falls as
    /// debris, and sometimes lands as a rubble pile.
    /// </summary>
    public partial class Destruction : Node3D
    {
        public const int Span = 3;
        const int X = VoxelWorld.SizeX, Y = VoxelWorld.SizeY, Z = VoxelWorld.SizeZ, Layer = X * Z;

        public VoxelWorld World;
        public Effects Fx;
        public Sfx Sound;
        readonly Random rng = new Random();
        readonly Dictionary<int, double> pending = new Dictionary<int, double>();
        short[] support;
        double integrityTimer, rumbleCooldown, thudCooldown;
        /// <summary>Concrete voxels destroyed this level (for the score).</summary>
        public int Blocks;
        /// <summary>Raised when collapse should shake the camera (strength 0-1).</summary>
        public event Action<float> Shake;
        /// <summary>Asks whether falling debris at (x, y, z) hits the player; return true if it did.</summary>
        public Func<Vector3, bool> HitsPlayer;
        /// <summary>Asks whether falling debris at (x, y, z) hits a guard; return true if it did.</summary>
        public Func<Vector3, bool> HitsGuard;
        /// <summary>Is the spot clear of people, so rubble may land there?</summary>
        public Func<Vector3, bool> SpotClear;

        sealed class Faller
        {
            public Vector3 P, V, Spin;
            public byte M;
            public MeshInstance3D Mesh;
            public bool Dead;
        }
        readonly List<Faller> fallers = new List<Faller>();
        BoxMesh fallerMesh;
        Material[] fallerMaterials = new Material[16];

        public override void _Ready()
        {
            fallerMesh = new BoxMesh { Size = new Vector3(.92f, .92f, .92f) };
        }

        public void SetMaterials(Material[] materials) => fallerMaterials = materials;

        public void Reset()
        {
            pending.Clear();
            foreach (var f in fallers) f.Mesh.QueueFree();
            fallers.Clear();
            integrityTimer = 0;
            Blocks = 0;
        }

        static bool IsDyn(byte m) => m == 2 || m == 7;
        static bool IsAnchor(byte m) => m == 1 || m == 3 || m == 6 || m >= 8;

        /// <summary>Voxel DDA: the first solid voxel along the ray within `limit`, or Material 0.</summary>
        public RayHit Trace(Vector3 from, Vector3 dir, double limit)
        {
            double x = from.X, y = from.Y, z = from.Z, dx = dir.X, dy = dir.Y, dz = dir.Z;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
            int sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1, sz = dz < 0 ? -1 : 1;
            double ax = dx == 0 ? double.PositiveInfinity : Math.Abs(1 / dx);
            double ay = dy == 0 ? double.PositiveInfinity : Math.Abs(1 / dy);
            double az = dz == 0 ? double.PositiveInfinity : Math.Abs(1 / dz);
            double tx = dx == 0 ? double.PositiveInfinity : (sx > 0 ? ix + 1 - x : x - ix) * ax;
            double ty = dy == 0 ? double.PositiveInfinity : (sy > 0 ? iy + 1 - y : y - iy) * ay;
            double tz = dz == 0 ? double.PositiveInfinity : (sz > 0 ? iz + 1 - z : z - iz) * az;
            double t = 0;
            int side = 0;
            while (t <= limit && VoxelWorld.Inside(ix, iy, iz))
            {
                byte m = World.Data[VoxelWorld.Index(ix, iy, iz)];
                if (m != 0) return new RayHit { X = ix, Y = iy, Z = iz, T = t, Side = side, Material = m };
                if (tx < ty && tx < tz) { t = tx; tx += ax; ix += sx; side = 0; }
                else if (ty < tz) { t = ty; ty += ay; iy += sy; side = 1; }
                else { t = tz; tz += az; iz += sz; side = 2; }
            }
            return new RayHit { T = double.PositiveInfinity };
        }

        /// <summary>Removes one voxel by a bullet (concrete or rubble only). Returns true if it went.</summary>
        public bool ShootOut(int x, int y, int z)
        {
            byte m = World.Tile(x, y, z);
            if (m != 2 && m != 7) return false;
            World.Set(x, y, z, 0);
            if (m == 2) Blocks++;
            Poke();
            return true;
        }

        /// <summary>
        /// Blows out a ball of wall: concrete, rubble, sandbags, drums and bars go; reinforced concrete
        /// only cracks into ordinary concrete (a second blast finishes it). Returns voxels removed.
        /// </summary>
        public int Carve(Vector3 c, double radius, Action<Vector3> onRemove = null)
        {
            int removed = 0;
            for (int y = (int)Math.Floor(c.Y - radius); y <= c.Y + radius; y++)
                for (int z = (int)Math.Floor(c.Z - radius); z <= c.Z + radius; z++)
                    for (int x = (int)Math.Floor(c.X - radius); x <= c.X + radius; x++)
                    {
                        byte m = World.Tile(x, y, z);
                        if (m != 2 && m != 7 && m != 8 && m != 9 && m != 10 && m != 11) continue;
                        double ddx = x + .5 - c.X, ddy = y + .5 - c.Y, ddz = z + .5 - c.Z;
                        if (ddx * ddx + ddy * ddy + ddz * ddz > radius * radius) continue;
                        if (m == 10) { World.Set(x, y, z, 2); continue; }
                        World.Set(x, y, z, 0);
                        removed++;
                        if (m == 2) Blocks++;
                        onRemove?.Invoke(new Vector3(x + .5f, y + .5f, z + .5f));
                    }
            if (removed > 0) Poke();
            return removed;
        }

        public int CountMaterial(byte m, Vector3 c, double r)
        {
            int n = 0;
            for (int y = (int)Math.Floor(c.Y - r); y <= c.Y + r; y++)
                for (int z = (int)Math.Floor(c.Z - r); z <= c.Z + r; z++)
                    for (int x = (int)Math.Floor(c.X - r); x <= c.X + r; x++)
                        if (World.Tile(x, y, z) == m) n++;
            return n;
        }

        /// <summary>Walking into knee-high rubble shoves it aside.</summary>
        public void KickRubble(double x, double z)
        {
            int k = 0;
            for (int iz = (int)Math.Floor(z - .45); iz <= (int)Math.Floor(z + .45); iz++)
                for (int ix = (int)Math.Floor(x - .45); ix <= (int)Math.Floor(x + .45); ix++)
                    for (int iy = 1; iy <= 2; iy++)
                        if (World.Tile(ix, iy, iz) == 7)
                        {
                            World.Set(ix, iy, iz, 0); k++;
                            Fx?.Burst(new Vector3(ix + .5f, iy + .5f, iz + .5f), 2);
                        }
            if (k > 0) Poke();
        }

        /// <summary>Schedules a support check shortly (several changes in one frame share one check).</summary>
        public void Poke(double delay = .05)
        {
            if (integrityTimer <= 0) integrityTimer = delay;
        }

        void CheckIntegrity()
        {
            support ??= new short[X * Y * Z];
            Array.Fill(support, short.MaxValue);
            var data = World.Data;
            var frontier = new List<int>();
            for (int y = 1; y < Y; y++)
                for (int z = 0; z < Z; z++)
                    for (int x = 0; x < X; x++)
                    {
                        int i = VoxelWorld.Index(x, y, z);
                        if (IsDyn(data[i]) && IsAnchor(data[i - Layer])) { support[i] = 0; frontier.Add(i); }
                    }
            for (int d = 0; d <= Span && frontier.Count > 0; d++)
            {
                var next = new List<int>();
                var stack = frontier;
                while (stack.Count > 0)
                {
                    int i = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    if (support[i] != d) continue;
                    int x = i % X, z = i / X % Z, y = i / Layer;
                    int up = i + Layer;
                    if (y + 1 < Y && IsDyn(data[up]) && support[up] > d) { support[up] = (short)d; stack.Add(up); }
                    int[] sides = { x > 0 ? i - 1 : -1, x < X - 1 ? i + 1 : -1, z > 0 ? i - X : -1, z < Z - 1 ? i + X : -1, y > 1 ? i - Layer : -1 };
                    foreach (int n in sides)
                        if (n >= 0 && IsDyn(data[n]) && support[n] > d + 1) { support[n] = (short)(d + 1); next.Add(n); }
                }
                frontier = next;
            }
            int fresh = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (IsDyn(data[i]) && support[i] > Span && !pending.ContainsKey(i))
                {
                    // higher voxels let go slightly later, so failure visibly runs up the wall
                    pending[i] = .08 + rng.NextDouble() * .32 + i / Layer * .015;
                    fresh++;
                }
            }
            if (fresh > 0 && rumbleCooldown <= 0)
            {
                Sound?.Play("collapse_rumble", .8f);
                rumbleCooldown = .5;
                Shake?.Invoke(.18f);
            }
        }

        public void Step(double dt)
        {
            rumbleCooldown -= dt; thudCooldown -= dt;
            if (integrityTimer > 0)
            {
                integrityTimer -= dt;
                if (integrityTimer <= 0) { integrityTimer = 0; CheckIntegrity(); }
            }
            int dropped = 0;
            List<int> due = null;
            foreach (var key in new List<int>(pending.Keys))
            {
                double left = pending[key] - dt;
                if (left > 0) pending[key] = left;
                else { pending.Remove(key); (due ??= new List<int>()).Add(key); }
            }
            if (due != null)
                foreach (int i in due)
                {
                    byte m = World.Data[i];
                    if (!IsDyn(m)) continue;
                    int x = i % X, z = i / X % Z, y = i / Layer;
                    World.Set(x, y, z, 0);
                    dropped++;
                    if (m == 2) Blocks++;
                    var f = new Faller
                    {
                        P = new Vector3(x + .5f, y + .5f, z + .5f),
                        V = new Vector3((float)(rng.NextDouble() - .5) * .6f, -(float)rng.NextDouble() * .5f, (float)(rng.NextDouble() - .5) * .6f),
                        Spin = new Vector3((float)(rng.NextDouble() - .5) * 8, (float)(rng.NextDouble() - .5) * 8, 0),
                        M = m,
                    };
                    f.Mesh = new MeshInstance3D { Mesh = fallerMesh, MaterialOverride = fallerMaterials[m], Position = f.P };
                    AddChild(f.Mesh);
                    fallers.Add(f);
                }
            if (dropped > 0) Poke(.06);   // removal may stress the next row: chain reaction

            foreach (var f in fallers)
            {
                f.V.Y -= 24 * (float)dt;
                f.P += f.V * (float)dt;
                f.Mesh.Position = f.P;
                f.Mesh.Rotation += f.Spin * (float)dt;
                if (f.V.Y < -3 && HitsPlayer != null && HitsPlayer(f.P)) { f.Dead = true; Fx?.Burst(f.P, 5); continue; }
                if (f.V.Y < -3 && f.P.Y < 4.3f && HitsGuard != null && HitsGuard(f.P)) { f.Dead = true; Fx?.Burst(f.P, 5); continue; }
                int fx = (int)Math.Floor(f.P.X), fz = (int)Math.Floor(f.P.Z), below = (int)Math.Floor(f.P.Y - .5f);
                if (World.Tile(fx, below, fz) != 0 || f.P.Y < 1.5f)
                {
                    f.Dead = true;
                    int cell = below + 1;
                    bool clear = SpotClear == null || SpotClear(f.P);
                    if (rng.NextDouble() < .45 && clear && cell < Y && World.Tile(fx, cell, fz) == 0)
                    {
                        World.Set(fx, cell, fz, 7);
                        Poke(.06);
                    }
                    Fx?.Burst(new Vector3(fx + .5f, cell, fz + .5f), 3);
                    if (thudCooldown <= 0) { Sound?.Play("debris_thud", .6f, 1 + (float)rng.NextDouble() * .3f); thudCooldown = .07; }
                }
            }
            for (int k = fallers.Count - 1; k >= 0; k--)
                if (fallers[k].Dead) { fallers[k].Mesh.QueueFree(); fallers.RemoveAt(k); }
        }
    }
}

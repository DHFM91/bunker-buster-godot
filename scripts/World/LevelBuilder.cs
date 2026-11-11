using System;
using System.Collections.Generic;

namespace BunkerBuster
{
    /// <summary>A wall lamp: position, the direction it faces out of the wall, reach and strength.</summary>
    public sealed class Lamp
    {
        public double X, Y, Z, Nx, Nz, Range, Power;
        public bool Dead;
    }

    /// <summary>A guard's sandbag post: where he stands and which way he watches.</summary>
    public sealed class Sentry
    {
        public double X, Z, Yaw;
        public int Cell;
    }

    public struct Rect
    {
        public int X0, Z0, X1, Z1;
        public Rect(int x0, int z0, int x1, int z1) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
    }

    public sealed class Truck
    {
        public int X0, Z0, X1, Z1;
        public string Along;
    }

    /// <summary>Everything the level stamping produced besides the voxels themselves.</summary>
    public sealed class Level
    {
        public LayoutPlan Plan;
        public Theme Theme;
        public readonly List<Lamp> Lamps = new List<Lamp>();
        public readonly List<Sentry> Sentries = new List<Sentry>();
        public readonly List<Rect> Yards = new List<Rect>();
        public readonly List<Truck> Trucks = new List<Truck>();
        public Rect? Foyer;
        public double ExtractionX, ExtractionZ, IntelX, IntelZ;
    }

    /// <summary>
    /// Port of reset()'s world stamping in bunker-buster.js: carve the rooms, apply the plan
    /// (passages, merged spaces, reinforced walls, gates, windows, vents) and dress the base.
    /// </summary>
    public static class LevelBuilder
    {
        const int Columns = MapPlan.Columns, Room = MapPlan.Room, WallTop = MapPlan.WallTop;
        const int X = VoxelWorld.SizeX, Z = VoxelWorld.SizeZ;

        public static Rect RoomBox(int cell)
        {
            var (x, z) = MapPlan.CellCenter(cell);
            return new Rect((int)(x - Room / 2.0), (int)(z - Room / 2.0), (int)(x + Room / 2.0), (int)(z + Room / 2.0));
        }

        public static Rect SpaceBox(Space s)
        {
            Rect a = RoomBox(s.Row * Columns + s.Col), b = RoomBox((s.Row + s.H - 1) * Columns + s.Col + s.W - 1);
            return new Rect(a.X0, a.Z0, b.X1, b.Z1);
        }

        /// <summary>The 3-voxel wall slab between two side-by-side rooms. AxisX: the rooms sit side by side along x.</summary>
        public static (Rect Box, bool AxisX) Slab(int a, int b)
        {
            Rect A = RoomBox(Math.Min(a, b)), B = RoomBox(Math.Max(a, b));
            return Math.Abs(a - b) == 1
                ? (new Rect(A.X1, A.Z0, B.X0, A.Z1), true)
                : (new Rect(A.X0, A.Z1, A.X1, B.Z0), false);
        }

        public static Level Build(VoxelWorld w, Theme theme, Func<double> random)
        {
            var level = new Level { Theme = theme };
            w.Clear();
            w.Box(0, 0, 0, X, 1, Z, 1);
            w.Box(0, 1, 0, 1, WallTop, Z, 3);
            w.Box(X - 1, 1, 0, X, WallTop, Z, 3);
            w.Box(0, 1, 0, X, WallTop, 1, 3);
            w.Box(0, 1, Z - 1, X, WallTop, Z, 3);
            w.Box(1, 1, 1, X - 1, WallTop, Z - 1, 2);
            for (int cell = 0; cell < Columns * MapPlan.Rows; cell++)
            {
                Rect r = RoomBox(cell);
                w.Box(r.X0, 1, r.Z0, r.X1, WallTop, r.Z1, 0);
            }

            var plan = MapPlan.Plan(random, theme.Plan);
            level.Plan = plan;
            foreach (var (pa, pb) in plan.Passages)
            {
                Rect a = RoomBox(pa), b = RoomBox(pb);
                w.Box(Math.Min(a.X0, b.X0), 1, Math.Min(a.Z0, b.Z0), Math.Max(a.X1, b.X1), WallTop, Math.Max(a.Z1, b.Z1), 0);
            }

            foreach (var s in plan.Spaces)
            {
                Rect b = SpaceBox(s);
                w.Box(b.X0, 1, b.Z0, b.X1, WallTop, b.Z1, 0);
                if (s.Kind == "foyer") level.Foyer = b;
                if (s.Kind == "atrium")
                {
                    // the wall crossings between the merged rooms stay up as reinforced pillars
                    for (int i = 0; i < s.W - 1; i++)
                        for (int j = 0; j < s.H - 1; j++)
                        {
                            Rect r = RoomBox((s.Row + j) * Columns + s.Col + i);
                            w.Box(r.X1, 1, r.Z1, r.X1 + 3, WallTop, r.Z1 + 3, 10);
                        }
                }
                if (s.Kind == "yard") { w.Box(b.X0, 0, b.Z0, b.X1, 1, b.Z1, 12); level.Yards.Add(b); }
            }

            foreach (var (a, b) in plan.Reinforced)
            {
                var (s, _) = Slab(a, b);
                w.Swap(s.X0, 1, s.Z0, s.X1, WallTop, s.Z1, 2, 10);
            }
            foreach (var (a, b) in plan.Barred)
            {
                // a gate of steel bars in the middle of the doorway, with a concrete lintel above it
                var (s, axisX) = Slab(a, b);
                if (axisX) { w.Box(s.X0 + 1, 1, s.Z0, s.X0 + 2, 5, s.Z1, 11); w.Box(s.X0 + 1, 5, s.Z0, s.X0 + 2, WallTop, s.Z1, 2); }
                else { w.Box(s.X0, 1, s.Z0 + 1, s.X1, 5, s.Z0 + 2, 11); w.Box(s.X0, 5, s.Z0 + 1, s.X1, WallTop, s.Z0 + 2, 2); }
            }
            foreach (var (a, b, bars) in plan.Windows)
            {
                // a slot from head height up, two cells tall, three wide; bars down the middle of some
                var (s, axisX) = Slab(a, b);
                if (axisX) { w.Box(s.X0, 3, s.Z0 + 1, s.X1, 5, s.Z1 - 1, 0); if (bars) w.Box(s.X0 + 1, 3, s.Z0 + 1, s.X0 + 2, 5, s.Z1 - 1, 11); }
                else { w.Box(s.X0 + 1, 3, s.Z0, s.X1 - 1, 5, s.Z1, 0); if (bars) w.Box(s.X0 + 1, 3, s.Z0 + 1, s.X1 - 1, 5, s.Z0 + 2, 11); }
            }
            foreach (var (a, b) in plan.Vents)
            {
                var (s, axisX) = Slab(a, b);
                if (axisX) w.Box(s.X0, 1, s.Z0 + 2, s.X1, 3, s.Z0 + 4, 0);
                else w.Box(s.X0 + 2, 1, s.Z0, s.X0 + 4, 3, s.Z1, 0);
            }

            var extraction = MapPlan.CellCenter(MapPlan.FirstCell);
            var intel = MapPlan.CellCenter(MapPlan.LastCell);
            level.ExtractionX = extraction.X; level.ExtractionZ = extraction.Z;
            level.IntelX = intel.X; level.IntelZ = intel.Z;
            int padX = (int)Math.Floor(extraction.X), padZ = (int)Math.Floor(extraction.Z);
            w.Box(padX - 2, 0, padZ - 2, padX + 3, 1, padZ + 3, 4);
            int intelX = (int)Math.Floor(intel.X), intelZ = (int)Math.Floor(intel.Z);
            w.Box(intelX, 1, intelZ, intelX + 1, 2, intelZ + 1, 6);

            // Foyer dressing: sandbag nests either side, crates and drums against the walls, floodlights behind the pad.
            if (level.Foyer is Rect f)
            {
                int cx = (f.X0 + f.X1) / 2;
                foreach (int dir in new[] { -1, 1 })
                {
                    int nx = dir < 0 ? f.X0 + 3 : f.X1 - 4;
                    for (int k = 0; k < 4; k++) w.Put(nx - dir * k, 1, f.Z0 + 7, 3, 8);
                    for (int k = 1; k < 3; k++) w.Put(nx, 1, f.Z0 + 7 + k, 3, 8);
                    int wallX = dir < 0 ? f.X0 : f.X1 - 1;
                    w.Put(wallX, 1, f.Z0, 3, 6); w.Put(wallX - dir, 1, f.Z0, 2, 6); w.Put(wallX, 1, f.Z0 + 1, 2, 6);
                    w.Put(wallX, 1, f.Z0 + 5, 3, 9); w.Put(wallX, 1, f.Z0 + 6, 3, 9);
                    AddLamp(level, cx + dir * 6.5, 7.2, f.Z0 + .06, 0, 1, 15, 1.5);
                }
            }
            foreach (var s in plan.Spaces)
            {
                if (s.Kind != "yard") continue;
                Rect b = SpaceBox(s);
                Truck t = StampTruck(w, level, b, s.Along, s.TruckShift);
                for (int n = 0; n < s.Cover; n++)
                {
                    // short sandbag lines and drum pairs, kept clear of the truck
                    uint h = VoxelWorld.H3(b.X0 + n, b.Z0, n + 7);
                    // JS: h is unsigned for the x pick, but (h>>8) is a signed 32-bit shift, so z can land below the yard.
                    int x = b.X0 + 1 + (int)(h % (uint)(b.X1 - b.X0 - 4));
                    int z = b.Z0 + 1 + unchecked((int)h >> 8) % (b.Z1 - b.Z0 - 3);
                    if (x + 3 > t.X0 - 1 && x < t.X1 + 1 && z > t.Z0 - 2 && z < t.Z1 + 1) continue;
                    for (int k = 0; k < 3; k++) w.Put(x + k, 1, z, ((h >> 4) & 1) != 0 ? 3 : 2, 8);
                    w.Put(x, 1, z + 1, 3, 9);
                }
                AddLamp(level, (b.X0 + b.X1) / 2.0, 7.4, b.Z0 + .06, 0, 1, 10, 1.1);
            }
            foreach (var p in plan.Props)
            {
                Rect b = RoomBox(p.Cell);
                if (p.Type == "lamp") { RoomLamp(w, level, p.Cell, p.Side); continue; }
                if (p.Type == "sandbags")
                {
                    int cx = (b.X0 + b.X1) / 2, cz = (b.Z0 + b.Z1) / 2;
                    int[][] spots = { new[] { cx, b.Z1 - 1, 1, 0 }, new[] { b.X1 - 1, cz, 0, 1 }, new[] { cx, b.Z0, 1, 0 }, new[] { b.X0, cz, 0, 1 } };
                    int ex = spots[p.Side][0], ez = spots[p.Side][1], ox = spots[p.Side][2], oz = spots[p.Side][3];
                    int wx = ex + (oz != 0 ? (p.Side == 1 ? 1 : -1) : 0), wz = ez + (ox != 0 ? (p.Side == 0 ? 1 : -1) : 0);
                    if (w.Tile(wx, 2, wz) == 0) continue;   // only against a wall
                    for (int k = -1; k <= 1; k++) w.Put(ex + ox * k, 1, ez + oz * k, 3, 8);
                    continue;
                }
                int px = (p.Corner & 1) != 0 ? b.X1 - 1 : b.X0, pz = (p.Corner & 2) != 0 ? b.Z1 - 1 : b.Z0, inx = (p.Corner & 1) != 0 ? -1 : 1;
                if (p.Type == "crates") { w.Put(px, 1, pz, (VoxelWorld.H3(px, pz, 1) & 1) != 0 ? 3 : 2, 6); w.Put(px + inx, 1, pz, 2, 6); }
                else { byte m = p.Fuel ? (byte)15 : (byte)9; w.Put(px, 1, pz, 3, m); w.Put(px + inx, 1, pz, 3, m); }
            }
            // Guard posts: a horseshoe of sandbags open toward where the sentry looks.
            int[][] facings = { new[] { 0, 1 }, new[] { 1, 0 }, new[] { 0, -1 }, new[] { -1, 0 } };
            foreach (var (cell, facing) in plan.Posts)
            {
                Rect b = RoomBox(cell);
                int cx = b.X0 + 2, cz = b.Z0 + 2, fx = facings[facing][0], fz = facings[facing][1];
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0 || dx == fx && fx != 0 || dz == fz && fz != 0) continue;
                        w.Put(cx + dx, 1, cz + dz, 3, 8);
                    }
                level.Sentries.Add(new Sentry { X = cx + .5, Z = cz + .5, Cell = cell, Yaw = Math.Atan2(fx, fz) });
            }
            w.MarkAllDirty();
            return level;
        }

        static void AddLamp(Level level, double x, double y, double z, double nx, double nz, double range = 7, double power = 1) =>
            level.Lamps.Add(new Lamp { X = x, Y = y, Z = z, Nx = nx, Nz = nz, Range = range, Power = power });

        /// <summary>A wall lamp in the middle of one side of a room, only if there is wall behind it.</summary>
        static void RoomLamp(VoxelWorld w, Level level, int cell, int side)
        {
            Rect b = RoomBox(cell);
            double cx = (b.X0 + b.X1) / 2.0, cz = (b.Z0 + b.Z1) / 2.0;
            double[][] spots = { new[] { cx, b.Z1 - .06, 0, -1 }, new[] { b.X1 - .06, cz, -1, 0 }, new[] { cx, b.Z0 + .06, 0, 1 }, new[] { b.X0 + .06, cz, 1, 0 } };
            double x = spots[side][0], z = spots[side][1], nx = spots[side][2], nz = spots[side][3];
            if (w.Tile((int)Math.Floor(x - nx * .5), 7, (int)Math.Floor(z - nz * .5)) == 0) return;
            AddLamp(level, x, 6.7, z, nx, nz);
        }

        /// <summary>A canvas-backed cargo truck: dark chassis and wheels, cab with windows, tall covered bed.</summary>
        static Truck StampTruck(VoxelWorld w, Level level, Rect b, string along, double shift)
        {
            const int len = 6, wid = 3;
            bool alongX = along == "x";
            int spanX = alongX ? len : wid, spanZ = alongX ? wid : len;
            int x0 = b.X0 + 2 + (int)Math.Floor(shift * (b.X1 - b.X0 - 4 - spanX + 1));
            int z0 = b.Z0 + 2 + (int)Math.Floor((1 - shift) * (b.Z1 - b.Z0 - 4 - spanZ + 1));
            for (int i = 0; i < len; i++)
                for (int j = 0; j < wid; j++)
                {
                    int x = alongX ? x0 + i : x0 + j, z = alongX ? z0 + j : z0 + i;
                    w.Box(x, 1, z, x + 1, 2, z + 1, 14);
                    if (i < 2) { w.Box(x, 2, z, x + 1, 3, z + 1, 13); w.Box(x, 3, z, x + 1, 4, z + 1, 14); }
                    else w.Box(x, 2, z, x + 1, 5, z + 1, 13);
                }
            var t = new Truck { X0 = x0, Z0 = z0, X1 = x0 + spanX, Z1 = z0 + spanZ, Along = along };
            level.Trucks.Add(t);
            return t;
        }
    }
}

using System;
using System.Collections.Generic;

namespace BunkerBuster
{
    /// <summary>
    /// The level as a block of voxels, one byte per voxel (the material id), exactly like the browser
    /// game's `world` array. One voxel is one Godot unit; voxel (x, y, z) fills the box from (x, y, z)
    /// to (x+1, y+1, z+1), with y pointing up.
    /// Materials: 0 air, 1 floor, 2 concrete, 3 outer wall, 4 pad, 6 crate, 7 rubble, 8 sandbags,
    /// 9 drum, 10 reinforced concrete, 11 steel bars, 12 yard asphalt, 13 truck body, 14 truck chassis,
    /// 15 fuel drum.
    /// </summary>
    public sealed class VoxelWorld
    {
        public const int SizeX = (MapPlan.Columns - 1) * MapPlan.Stride + MapPlan.Room + 2;
        public const int SizeY = 12;
        public const int SizeZ = (MapPlan.Rows - 1) * MapPlan.Stride + MapPlan.Room + 2;

        /// <summary>Chunks are 16 x 16 columns, full height, for meshing.</summary>
        public const int ChunkSize = 16;
        public const int ChunksX = (SizeX + ChunkSize - 1) / ChunkSize;
        public const int ChunksZ = (SizeZ + ChunkSize - 1) / ChunkSize;

        public readonly byte[] Data = new byte[SizeX * SizeY * SizeZ];

        /// <summary>Chunks whose mesh needs rebuilding, as cx + cz * ChunksX.</summary>
        public readonly HashSet<int> DirtyChunks = new HashSet<int>();

        public static bool Inside(int x, int y, int z) =>
            x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ;

        public static int Index(int x, int y, int z) => x + SizeX * (z + SizeZ * y);

        public byte Tile(int x, int y, int z) => Inside(x, y, z) ? Data[Index(x, y, z)] : (byte)0;

        public void Set(int x, int y, int z, byte material)
        {
            if (!Inside(x, y, z)) return;
            int i = Index(x, y, z);
            if (Data[i] == material) return;
            Data[i] = material;
            MarkDirty(x, z);
        }

        public void Clear()
        {
            Array.Clear(Data);
            MarkAllDirty();
        }

        /// <summary>Fills [x0,x1) x [y0,y1) x [z0,z1) with a material, clipped to the world.</summary>
        public void Box(int x0, int y0, int z0, int x1, int y1, int z1, byte material)
        {
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); z0 = Math.Max(0, z0);
            x1 = Math.Min(SizeX, x1); y1 = Math.Min(SizeY, y1); z1 = Math.Min(SizeZ, z1);
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                        Data[Index(x, y, z)] = material;
            MarkRegionDirty(x0, z0, x1, z1);
        }

        /// <summary>Replaces one material with another inside a box.</summary>
        public void Swap(int x0, int y0, int z0, int x1, int y1, int z1, byte from, byte to)
        {
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                        if (Inside(x, y, z) && Data[Index(x, y, z)] == from) Data[Index(x, y, z)] = to;
            MarkRegionDirty(x0, z0, x1, z1);
        }

        /// <summary>A one-voxel column from y0 up to y1, only if the bottom spot is empty (JS put()).</summary>
        public void Put(int x, int y0, int z, int y1, byte material)
        {
            if (Inside(x, y0, z) && Tile(x, y0, z) == 0) Box(x, y0, z, x + 1, y1, z + 1, material);
        }

        /// <summary>Can a body of this radius stand at (x, z) with its top at bodyTop? (JS canStand)</summary>
        public bool CanStand(double x, double z, double bodyTop, double radius = .44)
        {
            if (x < 1 + radius || z < 1 + radius || x > SizeX - 1 - radius || z > SizeZ - 1 - radius) return false;
            int top = (int)Math.Floor(bodyTop);
            for (int iy = 1; iy <= top; iy++)
                for (int iz = (int)Math.Floor(z - radius); iz <= (int)Math.Floor(z + radius); iz++)
                    for (int ix = (int)Math.Floor(x - radius); ix <= (int)Math.Floor(x + radius); ix++)
                        if (Tile(ix, iy, iz) != 0) return false;
            return true;
        }

        /// <summary>
        /// Does a body of this radius fit at (x, z) with its feet at height `feet` and the given height?
        /// (feet = 1 is standing on the ground floor.)
        /// </summary>
        public bool FitsAt(double x, double z, double feet, double height, double radius = .44)
        {
            if (x < 1 + radius || z < 1 + radius || x > SizeX - 1 - radius || z > SizeZ - 1 - radius) return false;
            int y0 = (int)Math.Floor(feet + .001), y1 = (int)Math.Ceiling(feet + height - .001) - 1;
            for (int iy = Math.Max(1, y0); iy <= y1; iy++)
                for (int iz = (int)Math.Floor(z - radius); iz <= (int)Math.Floor(z + radius); iz++)
                    for (int ix = (int)Math.Floor(x - radius); ix <= (int)Math.Floor(x + radius); ix++)
                        if (Tile(ix, iy, iz) != 0) return false;
            return true;
        }

        /// <summary>The height of the highest solid surface under a body at (x, z) whose feet are at `feet`.</summary>
        public double GroundUnder(double x, double z, double feet, double radius = .44)
        {
            for (int iy = (int)Math.Floor(feet - .001); iy >= 0; iy--)
                for (int iz = (int)Math.Floor(z - radius); iz <= (int)Math.Floor(z + radius); iz++)
                    for (int ix = (int)Math.Floor(x - radius); ix <= (int)Math.Floor(x + radius); ix++)
                        if (Tile(ix, iy, iz) != 0) return iy + 1;
            return 1;
        }

        public void MarkDirty(int x, int z)
        {
            int cx = Math.Clamp(x, 0, SizeX - 1) / ChunkSize, cz = Math.Clamp(z, 0, SizeZ - 1) / ChunkSize;
            DirtyChunks.Add(cx + cz * ChunksX);
            // a voxel on a chunk edge also changes which faces the neighbouring chunk shows
            int lx = x - cx * ChunkSize, lz = z - cz * ChunkSize;
            if (lx == 0 && cx > 0) DirtyChunks.Add(cx - 1 + cz * ChunksX);
            if (lx == ChunkSize - 1 && cx < ChunksX - 1) DirtyChunks.Add(cx + 1 + cz * ChunksX);
            if (lz == 0 && cz > 0) DirtyChunks.Add(cx + (cz - 1) * ChunksX);
            if (lz == ChunkSize - 1 && cz < ChunksZ - 1) DirtyChunks.Add(cx + (cz + 1) * ChunksX);
        }

        public void MarkRegionDirty(int x0, int z0, int x1, int z1)
        {
            int cx0 = Math.Clamp(x0 - 1, 0, SizeX - 1) / ChunkSize, cx1 = Math.Clamp(x1, 0, SizeX - 1) / ChunkSize;
            int cz0 = Math.Clamp(z0 - 1, 0, SizeZ - 1) / ChunkSize, cz1 = Math.Clamp(z1, 0, SizeZ - 1) / ChunkSize;
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                    DirtyChunks.Add(cx + cz * ChunksX);
        }

        public void MarkAllDirty()
        {
            for (int i = 0; i < ChunksX * ChunksZ; i++) DirtyChunks.Add(i);
        }

        /// <summary>The browser game's integer hash (JS h3), used for repeatable placement.</summary>
        public static uint H3(int a, int b, int c) =>
            unchecked((uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791));
    }
}

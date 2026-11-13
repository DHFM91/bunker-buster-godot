using System.Collections.Generic;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// Turns one 16 x 16 column chunk of the voxel world into a mesh, one surface per material.
    /// Full blocks only draw faces that touch air (or a see-through prop), so a chunk stays small.
    /// Props that aren't full blocks (drums, steel bars, rubble) are drawn as smaller boxes.
    /// Textures repeat every 4 voxels (64 px at 16 px per voxel), matching the browser game.
    /// </summary>
    public static class ChunkMesher
    {
        sealed class Buffers
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<float> Tangents = new List<float>();
            public readonly List<int> Indices = new List<int>();
        }

        /// <summary>Materials that aren't a full cube and don't hide what's behind them.</summary>
        public static bool IsShaped(byte m) => m == 7 || m == 9 || m == 11 || m == 15;

        static bool Hides(VoxelWorld w, int x, int y, int z)
        {
            if (!VoxelWorld.Inside(x, y, z)) return false;
            byte m = w.Data[VoxelWorld.Index(x, y, z)];
            return m != 0 && !IsShaped(m);
        }

        public static ArrayMesh Build(VoxelWorld w, int chunkX, int chunkZ, Material[] materials)
        {
            var buffers = new Dictionary<byte, Buffers>();
            int x0 = chunkX * VoxelWorld.ChunkSize, z0 = chunkZ * VoxelWorld.ChunkSize;
            int x1 = Mathf.Min(x0 + VoxelWorld.ChunkSize, VoxelWorld.SizeX), z1 = Mathf.Min(z0 + VoxelWorld.ChunkSize, VoxelWorld.SizeZ);
            for (int y = 0; y < VoxelWorld.SizeY; y++)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                    {
                        byte m = w.Data[VoxelWorld.Index(x, y, z)];
                        if (m == 0) continue;
                        if (!buffers.TryGetValue(m, out var b)) buffers[m] = b = new Buffers();
                        if (IsShaped(m)) { AddShape(w, b, m, x, y, z); continue; }
                        var lo = new Vector3(x, y, z);
                        AddBlockFaces(w, b, x, y, z, lo, lo + Vector3.One, true);
                    }

            var mesh = new ArrayMesh();
            foreach (var pair in buffers)
            {
                var b = pair.Value;
                if (b.Indices.Count == 0) continue;
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = b.Verts.ToArray();
                arrays[(int)Mesh.ArrayType.Normal] = b.Normals.ToArray();
                arrays[(int)Mesh.ArrayType.TexUV] = b.Uvs.ToArray();
                arrays[(int)Mesh.ArrayType.Color] = b.Colors.ToArray();
                arrays[(int)Mesh.ArrayType.Tangent] = b.Tangents.ToArray();
                arrays[(int)Mesh.ArrayType.Index] = b.Indices.ToArray();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, materials[pair.Key]);
            }
            return mesh;
        }

        /// <summary>
        /// Adds the faces of a box. With cull=true (full blocks) a face is skipped when the neighbouring
        /// voxel is a full block. Each corner gets a simple ambient-occlusion shade from its neighbours.
        /// </summary>
        static void AddBlockFaces(VoxelWorld w, Buffers b, int x, int y, int z, Vector3 lo, Vector3 hi, bool cull)
        {
            // +X
            if (!cull || !Hides(w, x + 1, y, z))
                Quad(w, b, new Vector3(hi.X, lo.Y, lo.Z), new Vector3(hi.X, hi.Y, lo.Z), new Vector3(hi.X, hi.Y, hi.Z), new Vector3(hi.X, lo.Y, hi.Z), Vector3.Right, cull, x + 1, y, z);
            // -X
            if (!cull || !Hides(w, x - 1, y, z))
                Quad(w, b, new Vector3(lo.X, lo.Y, hi.Z), new Vector3(lo.X, hi.Y, hi.Z), new Vector3(lo.X, hi.Y, lo.Z), new Vector3(lo.X, lo.Y, lo.Z), Vector3.Left, cull, x - 1, y, z);
            // +Y
            if (!cull || !Hides(w, x, y + 1, z))
                Quad(w, b, new Vector3(lo.X, hi.Y, lo.Z), new Vector3(lo.X, hi.Y, hi.Z), new Vector3(hi.X, hi.Y, hi.Z), new Vector3(hi.X, hi.Y, lo.Z), Vector3.Up, cull, x, y + 1, z);
            // -Y (floor undersides are never seen)
            if (y > 0 && (!cull || !Hides(w, x, y - 1, z)))
                Quad(w, b, new Vector3(lo.X, lo.Y, hi.Z), new Vector3(lo.X, lo.Y, lo.Z), new Vector3(hi.X, lo.Y, lo.Z), new Vector3(hi.X, lo.Y, hi.Z), Vector3.Down, cull, x, y - 1, z);
            // +Z
            if (!cull || !Hides(w, x, y, z + 1))
                Quad(w, b, new Vector3(hi.X, lo.Y, hi.Z), new Vector3(hi.X, hi.Y, hi.Z), new Vector3(lo.X, hi.Y, hi.Z), new Vector3(lo.X, lo.Y, hi.Z), Vector3.Back, cull, x, y, z + 1);
            // -Z
            if (!cull || !Hides(w, x, y, z - 1))
                Quad(w, b, new Vector3(lo.X, lo.Y, lo.Z), new Vector3(lo.X, hi.Y, lo.Z), new Vector3(hi.X, hi.Y, lo.Z), new Vector3(hi.X, lo.Y, lo.Z), Vector3.Forward, cull, x, y, z - 1);
        }

        static void Quad(VoxelWorld w, Buffers b, Vector3 a, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 n, bool shade, int ax, int ay, int az)
        {
            int start = b.Verts.Count;
            // Tangent frame for the normal maps, matching the UV layout below: T = direction of +U,
            // B = direction of +V; w says whether B = cross(N, T) or its opposite (the MikkTSpace convention).
            Vector3 t = n.X != 0 ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0);
            Vector3 bt = n.Y != 0 ? new Vector3(0, 0, 1) : new Vector3(0, -1, 0);
            float tw = n.Cross(t).Dot(bt) >= 0 ? 1 : -1;
            // a faint per-block brightness variation so big walls don't look stamped out of one tile
            var centre = (a + c2) * .5f - n * .5f;
            uint h = VoxelWorld.H3((int)Mathf.Floor(centre.X), (int)Mathf.Floor(centre.Y), (int)Mathf.Floor(centre.Z));
            float vary = .955f + (h % 10) * .009f;
            AddCorner(w, b, a, n, shade, ax, ay, az, t, tw, vary);
            AddCorner(w, b, c1, n, shade, ax, ay, az, t, tw, vary);
            AddCorner(w, b, c2, n, shade, ax, ay, az, t, tw, vary);
            AddCorner(w, b, c3, n, shade, ax, ay, az, t, tw, vary);
            // Two triangles, clockwise as seen from the front (Godot's front-face winding).
            // (the corners above run counter-clockwise when seen from outside, so they're added in reverse)
            b.Indices.Add(start); b.Indices.Add(start + 2); b.Indices.Add(start + 1);
            b.Indices.Add(start); b.Indices.Add(start + 3); b.Indices.Add(start + 2);
        }

        static void AddCorner(VoxelWorld w, Buffers b, Vector3 p, Vector3 n, bool shade, int ax, int ay, int az, Vector3 t, float tw, float vary)
        {
            b.Verts.Add(p);
            b.Normals.Add(n);
            // world-space UVs: 4 voxels per texture repeat, v runs down the wall
            Vector2 uv = n.X != 0 ? new Vector2(p.Z, VoxelWorld.SizeY - p.Y)
                : n.Z != 0 ? new Vector2(p.X, VoxelWorld.SizeY - p.Y)
                : new Vector2(p.X, p.Z);
            b.Uvs.Add(uv / 4f);
            Color c = shade ? CornerShade(w, p, n, ax, ay, az) : Colors.White;
            b.Colors.Add(new Color(c.R * vary, c.G * vary, c.B * vary));
            b.Tangents.Add(t.X); b.Tangents.Add(t.Y); b.Tangents.Add(t.Z); b.Tangents.Add(tw);
        }

        /// <summary>
        /// Classic voxel corner occlusion: darken a corner by how many of the three voxels
        /// around it (in front of the face) are solid. Gives soft shading where walls meet floors.
        /// </summary>
        static Color CornerShade(VoxelWorld w, Vector3 p, Vector3 n, int ax, int ay, int az)
        {
            // the two in-face axes, stepping toward this corner
            int sx = 0, sy = 0, sz = 0, tx = 0, ty = 0, tz = 0;
            if (n.X != 0) { sy = p.Y > ay + .5f ? 1 : -1; tz = p.Z > az + .5f ? 1 : -1; }
            else if (n.Y != 0) { sx = p.X > ax + .5f ? 1 : -1; tz = p.Z > az + .5f ? 1 : -1; }
            else { sx = p.X > ax + .5f ? 1 : -1; ty = p.Y > ay + .5f ? 1 : -1; }
            bool s1 = Hides(w, ax + sx, ay + sy, az + sz);
            bool s2 = Hides(w, ax + tx, ay + ty, az + tz);
            bool c = Hides(w, ax + sx + tx, ay + sy + ty, az + sz + tz);
            int level = s1 && s2 ? 3 : (s1 ? 1 : 0) + (s2 ? 1 : 0) + (c ? 1 : 0);
            float k = 1f - level * .17f;
            return new Color(k, k, k);
        }

        /// <summary>Smaller-than-a-block props, drawn as boxes inside the voxel.</summary>
        static void AddShape(VoxelWorld w, Buffers b, byte m, int x, int y, int z)
        {
            var o = new Vector3(x, y, z);
            if (m == 9 || m == 15)
            {
                // a drum: inset on all sides, with a darker lid band added by the texture
                AddBlockFaces(w, b, x, y, z, o + new Vector3(.12f, 0, .12f), o + new Vector3(.88f, 1, .88f), false);
            }
            else if (m == 11)
            {
                // steel bars: three thin posts across whichever way the bars run (along the wall they sit in)
                bool alongX = w.Tile(x - 1, y, z) == 11 || w.Tile(x + 1, y, z) == 11 || Hides(w, x - 1, y, z) || Hides(w, x + 1, y, z);
                for (int i = 0; i < 3; i++)
                {
                    float c = (i + .5f) / 3f;
                    Vector3 lo = alongX ? o + new Vector3(c - .05f, 0, .45f) : o + new Vector3(.45f, 0, c - .05f);
                    Vector3 hi = alongX ? o + new Vector3(c + .05f, 1, .55f) : o + new Vector3(.55f, 1, c + .05f);
                    AddBlockFaces(w, b, x, y, z, lo, hi, false);
                }
            }
            else
            {
                // rubble: a low heap, height varies per voxel
                float h = .35f + (VoxelWorld.H3(x, y, z) % 5) * .1f;
                AddBlockFaces(w, b, x, y, z, o + new Vector3(.05f, 0, .05f), o + new Vector3(.95f, h, .95f), false);
            }
        }
    }
}

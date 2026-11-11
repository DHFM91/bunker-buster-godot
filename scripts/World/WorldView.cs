using System;
using System.Collections.Generic;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// Everything you see of the level: voxel chunk meshes, sun and sky, wall lamps, and the
    /// intel and extraction markers. Rebuilds only the chunks the voxel world marks as dirty.
    /// </summary>
    public partial class WorldView : Node3D
    {
        static readonly Dictionary<int, string> TextureNames = new Dictionary<int, string>
        {
            { 1, "floor" }, { 2, "concrete" }, { 3, "outer_wall" }, { 4, "extraction_pad" }, { 6, "crate" },
            { 7, "rubble" }, { 8, "sandbag" }, { 9, "drum" }, { 10, "reinforced" }, { 11, "bars" },
            { 12, "yard" }, { 13, "truck" }, { 14, "truck_dark" }, { 15, "drum" },
        };

        public VoxelWorld World;
        public Level Level;
        Material[] materials;
        public Material[] Materials => materials;
        MeshInstance3D[] chunks;
        Node3D lampRoot;
        DirectionalLight3D sun;
        WorldEnvironment environment;
        public Sprite3D Intel;

        public override void _Ready()
        {
            materials = new Material[16];
            for (int i = 0; i < materials.Length; i++)
            {
                var mat = new StandardMaterial3D
                {
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
                    VertexColorUseAsAlbedo = true,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    Roughness = .92f,
                };
                if (TextureNames.TryGetValue(i, out var name))
                    mat.AlbedoTexture = GD.Load<Texture2D>($"res://assets/textures/{name}.png");
                if (i == 4) { mat.EmissionEnabled = true; mat.Emission = new Color(.05f, .35f, .2f); }
                materials[i] = mat;
            }
            chunks = new MeshInstance3D[VoxelWorld.ChunksX * VoxelWorld.ChunksZ];
            for (int i = 0; i < chunks.Length; i++)
            {
                chunks[i] = new MeshInstance3D { Name = "Chunk" + i, CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided };
                AddChild(chunks[i]);
            }
            lampRoot = new Node3D { Name = "Lamps" };
            AddChild(lampRoot);
            sun = new DirectionalLight3D { Name = "Sun", ShadowEnabled = true };
            sun.DirectionalShadowMaxDistance = 90;
            AddChild(sun);
            environment = new WorldEnvironment { Name = "Environment" };
            AddChild(environment);
            Intel = new Sprite3D
            {
                Name = "Intel",
                Texture = GD.Load<Texture2D>("res://assets/sprites/intel_case.png"),
                Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                PixelSize = .03f,
                Shaded = false,
            };
            AddChild(Intel);
        }

        /// <summary>Applies a freshly built level: lighting for a rolled mood, lamps, markers, all chunks.</summary>
        public void Show(Level level, Random rng)
        {
            Level = level;
            // this deploy's paint job: tint each material's texture to the rolled colours
            var palette = Palette.Roll(level.Theme, rng);
            for (int i = 1; i < materials.Length; i++)
            {
                var mat = (StandardMaterial3D)materials[i];
                Color t = palette.Tints[i];
                if (i == 15) t = new Color(t.R * 1.9f, t.G * .42f, t.B * .45f);
                mat.AlbedoColor = t;
            }
            string moodId = level.Theme.Moods[rng.Next(level.Theme.Moods.Length)];
            var mood = Mood.Get(moodId);
            double R(double a, double b) => a + rng.NextDouble() * (b - a);
            Color SkyShade(double[] v) => Mood.Hsl(v[0] + R(-12, 12), v[1] * R(.8, 1.2), v[2] * R(.92, 1.06));
            Color skyLo = SkyShade(mood.Lo), skyHi = SkyShade(mood.Hi);
            double el = R(mood.ElevMin, mood.ElevMax) * Math.PI / 180, az = R(0, Math.PI * 2);
            var sunDir = new Vector3((float)(Math.Cos(el) * Math.Sin(az)), (float)Math.Sin(el), (float)(Math.Cos(el) * Math.Cos(az)));
            sun.Transform = new Transform3D(Basis.LookingAt(-sunDir, Vector3.Up), Vector3.Zero);
            sun.LightColor = new Color((float)mood.Sun[0], (float)mood.Sun[1], (float)mood.Sun[2]);
            sun.LightEnergy = (float)(mood.K * 1.15);

            var skyMat = new ProceduralSkyMaterial
            {
                SkyTopColor = skyHi,
                SkyHorizonColor = skyLo,
                GroundHorizonColor = skyLo.Darkened(.25f),
                GroundBottomColor = skyLo.Darkened(.6f),
                SunAngleMax = 20,
            };
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = skyMat },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = (float)(mood.Ambient * 1.1),
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                SsaoEnabled = true,
                SsaoRadius = 1.2f,
                SsaoIntensity = 2.2f,
                FogEnabled = true,
                FogLightColor = skyLo.Lerp(skyHi, .25f),
                FogDensity = moodId == "smog" ? .02f : .008f,
                GlowEnabled = true,
                GlowIntensity = .4f,
            };
            if (level.Theme.NightVision)
            {
                // night-vision goggles: grey world, green phosphor light, brightened so nothing is hard to see
                env.AdjustmentEnabled = true;
                env.AdjustmentBrightness = 1.35f;
                env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
                env.AmbientLightColor = new Color(.35f, 1f, .45f);
                env.AmbientLightEnergy = 1.1f;
                sun.LightColor = new Color(.4f, 1f, .5f);
            }
            environment.Environment = env;

            foreach (Node child in lampRoot.GetChildren()) child.QueueFree();
            var lampColor = rng.NextDouble() < .6 ? new Color(1, .7f, .4f) : new Color(.82f, .95f, 1);
            var fixture = new BoxMesh { Size = new Vector3(.5f, .25f, .14f) };
            var fixtureMat = new StandardMaterial3D { EmissionEnabled = true, Emission = lampColor, EmissionEnergyMultiplier = 3, AlbedoColor = lampColor };
            foreach (var L in level.Lamps)
            {
                var pos = new Vector3((float)L.X, (float)L.Y, (float)L.Z);
                var light = new OmniLight3D
                {
                    Position = pos + new Vector3((float)L.Nx, 0, (float)L.Nz) * .35f,
                    OmniRange = (float)L.Range,
                    LightEnergy = (float)(L.Power * mood.Lamps * 1.6),
                    LightColor = lampColor,
                    OmniAttenuation = 1.4f,
                };
                lampRoot.AddChild(light);
                var box = new MeshInstance3D { Mesh = fixture, MaterialOverride = fixtureMat, Position = pos };
                if (L.Nx != 0) box.RotationDegrees = new Vector3(0, 90, 0);
                lampRoot.AddChild(box);
            }

            Intel.Position = new Vector3((float)Math.Floor(level.IntelX) + .5f, 2.5f, (float)Math.Floor(level.IntelZ) + .5f);
            Intel.Visible = true;
            World.MarkAllDirty();
            Refresh();
        }

        /// <summary>Rebuilds the meshes of chunks that changed since last time.</summary>
        public void Refresh()
        {
            if (World.DirtyChunks.Count == 0) return;
            foreach (int i in World.DirtyChunks)
                chunks[i].Mesh = ChunkMesher.Build(World, i % VoxelWorld.ChunksX, i / VoxelWorld.ChunksX, materials);
            World.DirtyChunks.Clear();
        }

        public override void _Process(double delta)
        {
            if (World != null) Refresh();
            if (Intel.Visible) Intel.Position = new Vector3(Intel.Position.X, 2.45f + .08f * Mathf.Sin(Time.GetTicksMsec() / 400f), Intel.Position.Z);
        }
    }
}

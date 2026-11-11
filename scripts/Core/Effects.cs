using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// One-shot particle effects: concrete chips, sparks, dust puffs, blast smoke and flashes.
    /// Each effect is a short-lived CPUParticles3D that frees itself when done.
    /// </summary>
    public partial class Effects : Node3D
    {
        BoxMesh chip, spark;
        StandardMaterial3D chipMat, sparkMat, dustMat, smokeMat;

        public override void _Ready()
        {
            chipMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1 };
            sparkMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color(1, .7f, .3f), EmissionEnergyMultiplier = 4 };
            dustMat = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            smokeMat = (StandardMaterial3D)dustMat.Duplicate();
            chip = new BoxMesh { Size = new Vector3(.12f, .12f, .12f), Material = chipMat };
            spark = new BoxMesh { Size = new Vector3(.04f, .04f, .16f), Material = sparkMat };
            var quad = new QuadMesh { Size = new Vector2(1, 1), Material = dustMat };
            dustQuad = quad;
            smokeQuad = new QuadMesh { Size = new Vector2(1, 1), Material = smokeMat };
        }

        QuadMesh dustQuad, smokeQuad;

        CpuParticles3D Emit(Vector3 at, Mesh mesh, int amount, float life, float speed, Color color, Vector3 gravity, float spread = 180)
        {
            var p = new CpuParticles3D
            {
                Mesh = mesh,
                Amount = Mathf.Max(1, amount),
                Lifetime = life,
                OneShot = true,
                Explosiveness = 1,
                Direction = Vector3.Up,
                Spread = spread,
                InitialVelocityMin = speed * .4f,
                InitialVelocityMax = speed,
                Gravity = gravity,
                Color = color,
                Position = at,
            };
            AddChild(p);
            p.Emitting = true;
            GetTree().CreateTimer(life + .5f).Timeout += p.QueueFree;
            return p;
        }

        /// <summary>A handful of concrete chips and a puff of dust.</summary>
        public void Burst(Vector3 at, int chips)
        {
            Emit(at, chip, chips, .45f, 3.5f, new Color(.6f, .6f, .58f), new Vector3(0, -16, 0));
            Dust(at, .9f, 1);
        }

        public void Sparks(Vector3 at, int n)
        {
            if (n <= 0) return;
            Emit(at, spark, n, .25f, 5, new Color(1, .8f, .45f), new Vector3(0, -12, 0));
        }

        public void Dust(Vector3 at, float life, int n, float size = 1)
        {
            var p = Emit(at, dustQuad, n, life, .5f, new Color(.7f, .68f, .64f, .45f), new Vector3(0, .3f, 0));
            p.ScaleAmountMin = size * .8f;
            p.ScaleAmountMax = size * 1.4f;
            p.ColorRamp = Fade(new Color(.7f, .68f, .64f, .5f));
        }

        /// <summary>The big one: flash, chips, sparks, a dust cloud and a smoke column that drifts up.</summary>
        public void Blast(Vector3 at, float scale = 1)
        {
            var flash = new OmniLight3D { Position = at, OmniRange = 14 * scale, LightEnergy = 9, LightColor = new Color(1, .7f, .35f) };
            AddChild(flash);
            var tween = CreateTween();
            tween.TweenProperty(flash, "light_energy", 0f, .42f);
            tween.TweenCallback(Callable.From(flash.QueueFree));
            Emit(at, chip, (int)(30 * scale), .9f, 7, new Color(.55f, .55f, .53f), new Vector3(0, -16, 0));
            Emit(at, spark, (int)(16 * scale), .4f, 9, new Color(1, .75f, .4f), new Vector3(0, -10, 0));
            var dust = Emit(at, dustQuad, (int)(22 * scale), 1.4f, 2.5f, Colors.White, new Vector3(0, .4f, 0));
            dust.ScaleAmountMin = 1.2f; dust.ScaleAmountMax = 2.6f;
            dust.ColorRamp = Fade(new Color(.72f, .68f, .6f, .6f));
            var smoke = Emit(at + Vector3.Up * .5f, smokeQuad, (int)(14 * scale), 5.5f, .7f, Colors.White, new Vector3(0, .5f, 0), 40);
            smoke.ScaleAmountMin = 1.6f; smoke.ScaleAmountMax = 3.4f;
            smoke.Explosiveness = .7f;
            smoke.ColorRamp = Fade(new Color(.16f, .15f, .14f, .7f));
        }

        static Gradient Fade(Color c)
        {
            var g = new Gradient();
            g.SetColor(0, c);
            g.SetColor(1, new Color(c.R, c.G, c.B, 0));
            return g;
        }
    }
}

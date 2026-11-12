using System;
using System.Threading.Tasks;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// Photo studio for the soldier model (run with `-- --modelshots`): renders one soldier in
    /// several poses from eight angles on a neutral backdrop, saves each as a PNG in
    /// logs/modelshots/, exports the standing soldier as a .glb 3D file, then quits.
    /// </summary>
    public partial class ModelShots : Node3D
    {
        static readonly (string Name, GuardAnim Anim, int Death)[] Poses =
        {
            ("low-ready", new GuardAnim { Alive = true }, 0),
            ("ready", new GuardAnim { Alive = true, Alert = true }, 0),
            ("aiming", new GuardAnim { Alive = true, Alert = true, Aim = .5f }, 0),
            ("firing", new GuardAnim { Alive = true, Alert = true, Aim = .05f, Muzzle = .1f }, 0),
            ("walking", new GuardAnim { Alive = true, Pace = 1, Gait = 1.1f }, 0),
            ("downed", new GuardAnim { Alive = false, Fall = 1 }, 0),
        };

        Camera3D cam;
        GuardModel model;
        string dir;

        public override void _Ready()
        {
            dir = ProjectSettings.GlobalizePath("res://logs/modelshots");
            System.IO.Directory.CreateDirectory(dir);
            GetWindow().Size = new Vector2I(900, 900);

            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("2b3433"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(.75f, .8f, .85f),
                AmbientLightEnergy = .55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                SsaoEnabled = true,
            };
            AddChild(new WorldEnvironment { Environment = env });
            var key = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.3f, LightColor = new Color(1, .96f, .9f) };
            AddChild(key);
            key.LookAtFromPosition(new Vector3(4, 8, 6), Vector3.Zero, Vector3.Up);
            var rim = new DirectionalLight3D { LightEnergy = .5f, LightColor = new Color(.7f, .85f, 1) };
            AddChild(rim);
            rim.LookAtFromPosition(new Vector3(-5, 4, -6), Vector3.Zero, Vector3.Up);
            var floor = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 3.2f, BottomRadius = 3.2f, Height = .1f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("4a5552"), Roughness = .95f },
                Position = new Vector3(0, -.05f, 0),
            };
            AddChild(floor);
            cam = new Camera3D { Fov = 30 };
            AddChild(cam);
            cam.MakeCurrent();
            _ = Run();
        }

        async Task Frames(int n)
        {
            for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        async Task Run()
        {
            for (int p = 0; p < Poses.Length; p++)
            {
                var (name, anim, death) = Poses[p];
                model?.QueueFree();
                model = new GuardModel { Name = "Soldier" };
                AddChild(model);
                model.Build(.30f, death);   // .30 = woodland camouflage, medium skin tone
                // let the pose (and the death fall) settle
                for (int k = 0; k < 90; k++) model.Animate(1 / 30.0, anim);
                if (name == "low-ready") ExportGlb();
                bool lying = !anim.Alive;
                for (int a = 0; a < 8; a++)
                {
                    float ang = a * Mathf.Tau / 8;   // 0 = front view
                    float dist = lying ? 10.5f : 11.5f, height = lying ? 4.5f : 3.2f;
                    var target = new Vector3(0, lying ? .4f : 2.15f, 0);
                    cam.LookAtFromPosition(new Vector3(Mathf.Sin(ang) * dist, height, Mathf.Cos(ang) * dist), target, Vector3.Up);
                    model.Animate(0, anim);
                    await Frames(3);
                    var img = GetViewport().GetTexture().GetImage();
                    img.SavePng(System.IO.Path.Combine(dir, $"{p}_{name}_{a * 45:000}.png"));
                }
            }
            GD.Print("model shots saved to " + dir);
            GetTree().Quit();
        }

        void ExportGlb()
        {
            try
            {
                var doc = new GltfDocument();
                var state = new GltfState();
                var err = doc.AppendFromScene(model, state);
                if (err == Error.Ok) err = doc.WriteToFilesystem(state, System.IO.Path.Combine(dir, "bunker-buster-soldier.glb"));
                GD.Print("glb export: " + err);
            }
            catch (Exception ex) { GD.Print("glb export failed: " + ex.Message); }
        }
    }
}

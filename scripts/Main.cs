using System;
using System.Linq;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// Game root. Builds a level, runs the briefing / playing / cleared loop, and hosts the
    /// command-line self-tests (run with `-- --selftest`).
    /// </summary>
    public partial class Main : Node3D
    {
        public const string Version = "g1";

        enum State { Briefing, Playing, Paused, Cleared }

        VoxelWorld world;
        WorldView view;
        PlayerController player;
        Hud hud;
        Node3D guardRoot;
        Level level;
        Theme theme;
        readonly Random rng = new Random();
        State state;
        int levelNumber = 1;
        double levelTime, runTime;
        bool hasIntel;
        uint seed;

        public override void _Ready()
        {
            if (OS.GetCmdlineUserArgs().Contains("--selftest"))
            {
                RunSelfTests();
                return;
            }
            world = new VoxelWorld();
            view = new WorldView { Name = "WorldView", World = world };
            AddChild(view);
            guardRoot = new Node3D { Name = "Guards" };
            AddChild(guardRoot);
            player = new PlayerController { Name = "Player", World = world };
            AddChild(player);
            hud = new Hud { Name = "Hud" };
            AddChild(hud);
            theme = Theme.All[0];
            NewLevel();
        }

        void NewLevel()
        {
            seed = (uint)rng.Next();
            level = LevelBuilder.Build(world, theme, new Mulberry32(seed).AsFunc());
            view.Show(level, rng);
            player.Spawn(level.ExtractionX, level.ExtractionZ);
            PlaceSentries();
            hasIntel = false;
            levelTime = 0;
            state = State.Briefing;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            hud.ShowOverlay(levelNumber == 1 ? "BUNKER BUSTER" : theme.Name,
                $"LEVEL {levelNumber} / {theme.Name}\n\n{theme.Brief}",
                "Click to deploy   ·   WASD move · mouse look · C crouch · Esc pause · F9 new base");
        }

        /// <summary>Placeholder guards: a sentry standing at each sandbag post (they don't move yet).</summary>
        void PlaceSentries()
        {
            foreach (Node child in guardRoot.GetChildren()) child.QueueFree();
            var sheet = GD.Load<Texture2D>("res://assets/sprites/guard_sheet.png");
            foreach (var s in level.Sentries)
            {
                var sprite = new Sprite3D
                {
                    Texture = sheet,
                    RegionEnabled = true,
                    RegionRect = new Rect2(0, 0, 48, 89),
                    Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    PixelSize = .049f,
                    AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                    Position = new Vector3((float)s.X, 1 + 89 * .049f / 2, (float)s.Z),
                };
                guardRoot.AddChild(sprite);
            }
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (state == State.Briefing || state == State.Paused) Deploy();
                else if (state == State.Cleared) { levelNumber++; theme = PickTheme(); NewLevel(); }
            }
            if (e is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.Escape && state == State.Playing)
                {
                    state = State.Paused;
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    hud.ShowOverlay("PAUSED", $"{theme.Name}  ·  level {levelNumber}", "Click to resume");
                }
                if (key.Keycode == Key.F9) NewLevel();
            }
        }

        void Deploy()
        {
            state = State.Playing;
            hud.HideOverlay();
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        Theme PickTheme()
        {
            var pool = Theme.All.Where(t => t != theme).ToArray();
            return pool[rng.Next(pool.Length)];
        }

        public override void _Process(double delta)
        {
            if (player == null) return;
            if (state == State.Playing)
            {
                levelTime += delta;
                runTime += delta;
                player.Step(delta);
                CheckObjectives();
            }
            double tx = hasIntel ? level.ExtractionX : level.IntelX, tz = hasIntel ? level.ExtractionZ : level.IntelZ;
            double dist = Math.Sqrt((tx - player.X) * (tx - player.X) + (tz - player.Z) * (tz - player.Z));
            // bearing to the objective relative to where you face: 0 = straight ahead
            double heading = Math.Atan2(-(tx - player.X), -(tz - player.Z));
            double rel = Mathf.Wrap((float)(heading - player.Yaw), -Mathf.Pi, Mathf.Pi);
            string arrow = Math.Abs(rel) < .4 ? "↑" : rel > 0 ? (rel > 2.4 ? "↓" : "←") : (rel < -2.4 ? "↓" : "→");
            hud.SetStatus(
                $"LEVEL {levelNumber} · {theme.Name}\n{(hasIntel ? "EXTRACT: get to the green pad" : "FIND THE INTEL (orange case)")}  {arrow} {dist:0}m{(player.Crouched ? "\nCROUCHED" : "")}",
                $"{FormatTime(levelTime)}\nBunker Buster {Version} · {Engine.GetFramesPerSecond()} fps");
        }

        void CheckObjectives()
        {
            if (!hasIntel)
            {
                double dx = Math.Floor(level.IntelX) + .5 - player.X, dz = Math.Floor(level.IntelZ) + .5 - player.Z;
                if (dx * dx + dz * dz < 1.7 * 1.7)
                {
                    hasIntel = true;
                    view.Intel.Visible = false;
                    hud.Say("INTEL SECURED. Get back to the pad.");
                }
            }
            else
            {
                double dx = level.ExtractionX - player.X, dz = level.ExtractionZ - player.Z;
                if (dx * dx + dz * dz < 2.5 * 2.5)
                {
                    state = State.Cleared;
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    hud.ShowOverlay("EXTRACTED", $"Level {levelNumber} cleared in {FormatTime(levelTime)}.\nRun time {FormatTime(runTime)}.", "Click for the next base");
                }
            }
        }

        static string FormatTime(double t)
        {
            int m = (int)(t / 60);
            double s = t - m * 60;
            return $"{m}:{s:00.00}";
        }

        /// <summary>Runs the C#-vs-browser checks and writes the report to logs/selftest.txt, then quits.</summary>
        void RunSelfTests()
        {
            string report = "Bunker Buster " + Version + " self-test\n";
            try
            {
                report += MapPlanSelfTest.Run(FileAccess.GetFileAsString("res://reference/map-plan-reference.json")) + "\n";
            }
            catch (Exception ex) { report += "MapPlan test crashed: " + ex + "\n"; }
            try
            {
                report += LevelSelfTest.Run(FileAccess.GetFileAsString("res://reference/level-reference.json")) + "\n";
            }
            catch (Exception ex) { report += "Level test crashed: " + ex + "\n"; }
            GD.Print(report);
            string dir = ProjectSettings.GlobalizePath("res://logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "selftest.txt"), report);
            GetTree().Quit();
        }
    }
}

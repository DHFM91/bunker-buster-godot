using System;
using System.Linq;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// Game root. Builds a level, runs the briefing / playing / results loop and the endless
    /// campaign, and hosts the command-line self-tests (run with `-- --selftest`).
    /// </summary>
    public partial class Main : Node3D
    {
        public const string Version = "g2";

        enum State { Briefing, Playing, Paused, Won, Lost }

        VoxelWorld world;
        WorldView view;
        Destruction destruction;
        GuardDirector guards;
        PlayerController player;
        PlayerCombat combat;
        ViewModel gun;
        Effects fx;
        Sfx sfx;
        Hud hud;
        Level level;
        Theme theme;
        readonly Random rng = new Random();
        State state;
        int levelNumber = 1, levelsCleared, runTotal;
        double levelTime, runTime, shake;
        bool hasIntel;

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
            sfx = new Sfx { Name = "Sfx" };
            AddChild(sfx);
            fx = new Effects { Name = "Effects" };
            AddChild(fx);
            destruction = new Destruction { Name = "Destruction", World = world, Fx = fx, Sound = sfx };
            AddChild(destruction);
            player = new PlayerController { Name = "Player", World = world };
            AddChild(player);
            guards = new GuardDirector { Name = "Guards", World = world, Destruction = destruction, Player = player, Fx = fx, Sound = sfx };
            AddChild(guards);
            hud = new Hud { Name = "Hud" };
            AddChild(hud);
            var gunLayer = new CanvasLayer { Name = "GunLayer", Layer = 0 };
            AddChild(gunLayer);
            gun = new ViewModel { Name = "ViewModel" };
            gun.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            gunLayer.AddChild(gun);
            combat = new PlayerCombat
            {
                Name = "Combat", World = world, Player = player, Destruction = destruction, Guards = guards,
                Fx = fx, Sound = sfx, Gun = gun,
            };
            AddChild(combat);

            guards.Say = t => hud.Say(t);
            combat.Say = t => hud.Say(t);
            guards.PlayerShot = dmg => combat.Damage(dmg, "You were hit before reaching extraction.");
            combat.Died = why => Finish(false, why);
            combat.Shake = s => shake = Math.Max(shake, s);
            destruction.Shake += s => shake = Math.Max(shake, s);
            destruction.HitsPlayer = p =>
            {
                if (p.Y <= 1 || p.Y >= player.Eye + .4 || Math.Abs(p.X - player.X) >= .75 || Math.Abs(p.Z - player.Z) >= .75) return false;
                hud.Say("FALLING DEBRIS");
                combat.Damage(8, "Crushed under falling concrete.");
                return true;
            };
            destruction.HitsGuard = p => guards.DebrisHits(p);
            destruction.SpotClear = p =>
                Math.Sqrt((p.X - player.X) * (p.X - player.X) + (p.Z - player.Z) * (p.Z - player.Z)) > 1.1 && !guards.NearAnyGuard(p, 1);

            theme = Theme.All[0];
            NewLevel();
        }

        void NewLevel()
        {
            uint seed = (uint)rng.Next();
            level = LevelBuilder.Build(world, theme, new Mulberry32(seed).AsFunc());
            view.Show(level, rng);
            destruction.SetMaterials(view.Materials);
            destruction.Reset();
            player.Spawn(level.ExtractionX, level.ExtractionZ);
            guards.Spawn(level, levelNumber);
            combat.Reset(theme);
            combat.Active = false;
            hasIntel = false;
            levelTime = 0;
            state = State.Briefing;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            string intro = levelNumber == 1
                ? "Grab the orange intel case and get back to the green extraction pad. Take the corridors, or blow your own shortcut through the concrete."
                : theme.Brief + (runTotal > 0 ? $"  Run total so far: {runTotal:N0}." : "");
            hud.ShowOverlay(levelNumber == 1 ? "BUNKER BUSTER" : theme.Name, $"LEVEL {levelNumber} / {theme.Name}\n\n{intro}",
                "Click to deploy\nWASD move · mouse aim · click fire · R reload · Q breach charge · G grenade · C crouch · Esc pause");
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (state == State.Briefing || state == State.Paused) { Deploy(); GetViewport().SetInputAsHandled(); }
                else if (state == State.Won) { levelNumber++; theme = PickTheme(); NewLevel(); }
                else if (state == State.Lost) { levelNumber = 1; levelsCleared = 0; runTotal = 0; runTime = 0; theme = Theme.All[0]; NewLevel(); }
            }
            if (e is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.Escape && state == State.Playing)
                {
                    state = State.Paused;
                    combat.Active = false;
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    hud.ShowOverlay("PAUSED", $"{theme.Name}  ·  level {levelNumber}", "Click to resume");
                }
                if (state != State.Playing) return;
                if (key.Keycode == Key.F9) { NewLevel(); return; }
                if (e.IsAction("reload")) combat.StartReload();
                if (e.IsAction("plant_charge")) combat.PlantCharge();
                if (e.IsAction("grenade")) combat.ThrowGrenade();
            }
        }

        void Deploy()
        {
            state = State.Playing;
            combat.Active = true;
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
            double dt = Math.Min(delta, .05);
            if (state == State.Playing)
            {
                levelTime += dt;
                runTime += dt;
                double oldX = player.X, oldZ = player.Z;
                player.Step(dt);
                if (player.Travel > 0)
                {
                    double mx = player.X - oldX, mz = player.Z - oldZ, ml = Math.Sqrt(mx * mx + mz * mz);
                    destruction.KickRubble(player.X + mx / ml * .55, player.Z + mz / ml * .55);
                }
                combat.Step(dt);
                if (state == State.Playing) guards.Step(dt);
                if (state == State.Playing) destruction.Step(dt);
                if (state == State.Playing) CheckObjectives();
            }
            gun.Sway = (float)Math.Min(1, player.Travel / (dt * 5));
            gun.StridePhase = (float)player.StridePhase;
            gun.Visible = state != State.Briefing;

            // camera shake from blasts and collapse
            shake = Math.Max(0, shake - dt);
            float k = (float)(shake * shake * .35);
            player.Camera.HOffset = (float)(rng.NextDouble() - .5) * k;
            player.Camera.VOffset = (float)(rng.NextDouble() - .5) * k;

            double tx = hasIntel ? level.ExtractionX : level.IntelX, tz = hasIntel ? level.ExtractionZ : level.IntelZ;
            double dist = Math.Sqrt((tx - player.X) * (tx - player.X) + (tz - player.Z) * (tz - player.Z));
            double heading = Math.Atan2(-(tx - player.X), -(tz - player.Z));
            double rel = Mathf.Wrap((float)(heading - player.Yaw), -Mathf.Pi, Mathf.Pi);
            string arrow = Math.Abs(rel) < .4 ? "↑" : rel > 0 ? (rel > 2.4 ? "↓" : "←") : (rel < -2.4 ? "↓" : "→");
            hud.SetStatus(
                $"LEVEL {levelNumber} · {theme.Name}\n{(hasIntel ? "EXTRACT: get to the green pad" : "FIND THE INTEL (orange case)")}  {arrow} {dist:0}m{(player.Crouched ? "\nCROUCHED" : "")}",
                $"{ScoreCalculator.FormatTime(levelTime)}\nBunker Buster {Version} · {Engine.GetFramesPerSecond()} fps");
            string ammo = combat.Reload > 0 ? "RELOADING" : $"{combat.Magazine} / {combat.Reserve}";
            hud.SetKit($"{ammo}\n{combat.Charges} CHG · {combat.GrenadeCount} GRN", combat.Hp, combat.Hurt);
            var P = GuardDirector.Phases[guards.AlertLevel];
            hud.SetAlert(guards.AlertLevel, P.Name, guards.AlertTimer, P.Time > 0 ? guards.AlertTimer / P.Time : 0);
        }

        void CheckObjectives()
        {
            if (!hasIntel)
            {
                double dx = level.IntelX - player.X, dz = level.IntelZ - player.Z;
                if (dx * dx + dz * dz < 1.9 * 1.9)
                {
                    hasIntel = true;
                    view.Intel.Visible = false;
                    sfx.Play("intel_pickup");
                    hud.Say("INTEL SECURED. RETURN TO THE GREEN PAD.");
                }
            }
            else
            {
                double dx = level.ExtractionX - player.X, dz = level.ExtractionZ - player.Z;
                if (dx * dx + dz * dz < 2.6 * 2.6) Finish(true, null);
            }
        }

        /// <summary>
        /// End of a level (JS finish + scoreRun). Damage costs half a percent per HP lost; dying
        /// costs a flat 50%; each level after the first is worth 10% more.
        /// </summary>
        void Finish(bool won, string why)
        {
            if (state != State.Playing) return;
            state = won ? State.Won : State.Lost;
            combat.Active = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            var r = ScoreCalculator.Calculate(won, levelTime, guards.Downed, destruction.Blocks, combat.Hp, levelNumber);
            runTotal += r.Total;
            if (won) levelsCleared++;
            string table = string.Join("\n", r.Lines.Select(l => $"{l.Label,-20} {l.Detail,-14} {l.Points,8:N0}"));
            table += $"\n\nSubtotal {r.Subtotal:N0}   damage −{r.Cut}% ({r.Penalty:N0})   level ×{r.Multiplier:0.0}\nLEVEL SCORE {r.Total:N0}     RUN TOTAL {runTotal:N0}";
            if (won)
                hud.ShowOverlay($"LEVEL {levelNumber} CLEAR", $"Intel secured in {ScoreCalculator.FormatTime(levelTime)}.\n\n{table}", "Click for the next base");
            else
                hud.ShowOverlay("MISSION FAILED", $"{why ?? "You were hit before reaching extraction."} Run over: {levelsCleared} level{(levelsCleared == 1 ? "" : "s")} cleared.\n\n{table}", "Click for a new run");
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

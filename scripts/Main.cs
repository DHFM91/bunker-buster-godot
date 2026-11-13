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
        public const string Version = "g5";

        enum State { Briefing, Playing, Paused, Won, Lost, RunOver, Initials }

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
        Leaderboard leaderboard;
        string initials = "", runSummary = "";
        ScoreEntry justAdded;
        Level level;
        Theme theme;
        readonly Random rng = new Random();
        State state;
        int levelNumber = 1, levelsCleared, runTotal;
        double levelTime, runTime, shake;
        bool hasIntel, smoke, bench;
        double benchTime;
        bool benchShot;
        readonly System.Collections.Generic.List<double> benchFrames = new System.Collections.Generic.List<double>();
        double smokeTime;
        int smokeStep, smokeMag;
        double smokeMaxFeet;
        readonly System.Text.StringBuilder smokeLog = new System.Text.StringBuilder();

        public override void _Ready()
        {
            if (OS.GetCmdlineUserArgs().Contains("--selftest"))
            {
                RunSelfTests();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--modelshots"))
            {
                AddChild(new ModelShots { Name = "ModelShots" });
                return;
            }
            smoke = OS.GetCmdlineUserArgs().Contains("--smoketest");
            bench = OS.GetCmdlineUserArgs().Contains("--benchmark");
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
            leaderboard = new Leaderboard { Name = "Leaderboard" };
            AddChild(leaderboard);
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
                if (p.Y <= player.Feet || p.Y >= player.Eye + .4 || Math.Abs(p.X - player.X) >= .75 || Math.Abs(p.Z - player.Z) >= .75) return false;
                hud.Say("FALLING DEBRIS");
                combat.Damage(8, "Crushed under falling concrete.");
                return true;
            };
            destruction.HitsGuard = p => guards.DebrisHits(p);
            destruction.SpotClear = p =>
                Math.Sqrt((p.X - player.X) * (p.X - player.X) + (p.Z - player.Z) * (p.Z - player.Z)) > 1.1 && !guards.NearAnyGuard(p, 1);

            theme = Theme.All[0];
            NewLevel();
            if (smoke || bench) { Deploy(); Input.MouseMode = Input.MouseModeEnum.Visible; }
            if (bench) { GetWindow().Size = new Vector2I(1600, 900); Engine.MaxFps = 0; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); }
        }

        /// <summary>
        /// Frame-rate benchmark (run with `-- --benchmark`): fixed level, vsync off, 1600x900; the camera
        /// sweeps through the foyer and down the main corridor for 12 s (guards active), then logs the
        /// average fps, the 1% low and the draw-call count to logs/benchmark.txt.
        /// </summary>
        void BenchStep(double delta)
        {
            benchTime += delta;
            if (benchTime > 1.5) benchFrames.Add(delta);   // skip warm-up
            double t = Math.Min(benchTime, 13.5);
            // walk forward from the pad into the base while slowly panning left and right
            player.X = level.ExtractionX + Math.Sin(t * .35) * 2.5;
            player.Z = level.ExtractionZ + t * 1.6;
            if (!world.CanStand(player.X, player.Z, 4.4)) player.X = level.ExtractionX;
            player.Yaw = (float)(Math.PI + Math.Sin(t * .6) * 1.1);
            player.Pitch = (float)(Math.Sin(t * .9) * .15);
            player.Step(0);
            guards.Step(delta);
            destruction.Step(delta);
            if (benchTime > 7 && !benchShot)
            {
                benchShot = true;
                string shotDir = ProjectSettings.GlobalizePath("res://logs");
                System.IO.Directory.CreateDirectory(shotDir);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, "benchmark.png"));
            }
            if (benchTime < 13.5) return;
            benchFrames.Sort();
            double sum = 0; foreach (var f in benchFrames) sum += f;
            double avg = benchFrames.Count / sum;
            int n1 = Math.Max(1, benchFrames.Count / 100);
            double worst = 0; for (int i = benchFrames.Count - n1; i < benchFrames.Count; i++) worst += benchFrames[i];
            double low1 = n1 / worst;
            string report = $"benchmark {Version}: {benchFrames.Count} frames, average {avg:0} fps, 1% low {low1:0} fps, " +
                $"draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}, " +
                $"primitives {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)}, " +
                $"video memory {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:0} MB, " +
                $"adapter {RenderingServer.GetVideoAdapterName()}";
            GD.Print(report);
            string dir = ProjectSettings.GlobalizePath("res://logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "benchmark.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + report + "\n");
            GetTree().Quit();
            bench = false;
        }

        void SmokeLog(string line)
        {
            smokeLog.AppendLine($"[{smokeTime,6:0.00}s] {line}");
            GD.Print(line);
        }

        /// <summary>
        /// Automated play-test (run with `-- --smoketest`): aims at walls, fires, plants a charge,
        /// throws a grenade, walks, and lets the guards react, logging what happened to logs/smoketest.txt.
        /// </summary>
        void SmokeStep(double dt)
        {
            smokeTime += dt;
            switch (smokeStep)
            {
                case 0:
                    SmokeLog($"level built: {level.Theme.Name}, guards {guards.Guards.Count}, lamps {level.Lamps.Count}, hp {combat.Hp}, mag {combat.Magazine}");
                    // turn until the nearest thing in view is breakable concrete within 12 m
                    player.Pitch = -.05f;
                    for (int k = 0; k < 72; k++)
                    {
                        player.Yaw = Mathf.Pi + k * Mathf.Tau / 72;
                        player.Step(0);
                        var probe = destruction.Trace(new Vector3((float)player.X, (float)player.Eye, (float)player.Z), player.LookDirection, 12);
                        if (probe.Material == 2) break;
                    }
                    smokeStep++; break;
                case 1:
                    if (smokeTime < 3.5) break;
                    SmokeLog($"after 3s standing still at spawn: alert {GuardDirector.Phases[guards.AlertLevel].Name}, guards who see you {guards.Guards.Count(g => g.Saw)}, hp {combat.Hp}, mag {combat.Magazine}");
                    var hit = destruction.Trace(new Vector3((float)player.X, (float)player.Eye, (float)player.Z), player.LookDirection, 60);
                    SmokeLog($"aim ray hits material {hit.Material} at {hit.T:0.0}m");
                    for (int i = 0; i < 6; i++) { combat.Cooldown = 0; combat.Fire(); }
                    SmokeLog($"fired 6: magazine {combat.Magazine}, concrete destroyed {destruction.Blocks}, alert {GuardDirector.Phases[guards.AlertLevel].Name}");
                    combat.Mode = PlayerCombat.FireMode.Burst; combat.TriggerArmed = true; combat.Cooldown = 0;
                    smokeMag = combat.Magazine;
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                    combat.TriggerPulled();
                    smokeStep++; break;
                case 2:
                    if (smokeTime < 4.5) break;
                    SmokeLog($"3-round burst: magazine {smokeMag} -> {combat.Magazine} (expect 3 fewer)");
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    combat.Mode = PlayerCombat.FireMode.Single;
                    {
                        // step up to the wall so it's within planting range
                        var eye = new Vector3((float)player.X, (float)player.Eye, (float)player.Z);
                        var wall = destruction.Trace(eye, player.LookDirection, 30);
                        if (wall.Hit && wall.T > 3.5)
                        {
                            var d = player.LookDirection; d.Y = 0; d = d.Normalized();
                            double nx = player.X + d.X * (wall.T - 3), nz = player.Z + d.Z * (wall.T - 3);
                            if (world.CanStand(nx, nz, 4.4)) { player.X = nx; player.Z = nz; player.Step(0); }
                        }
                        SmokeLog($"facing material {destruction.Trace(new Vector3((float)player.X, (float)player.Eye, (float)player.Z), player.LookDirection, 30).Material} for the charge");
                    }
                    combat.PlantCharge();
                    SmokeLog($"plant charge: charges left before fuse {combat.Charges}");
                    smokeStep++; break;
                case 3:
                    if (smokeTime < 8) break;
                    SmokeLog($"after charge: charges {combat.Charges}, concrete destroyed {destruction.Blocks}, hp {combat.Hp}, alert {GuardDirector.Phases[guards.AlertLevel].Name}, chunks dirty {world.DirtyChunks.Count}");
                    player.Yaw = Mathf.Pi; player.Pitch = .25f;
                    combat.ThrowGrenade();
                    SmokeLog($"grenade thrown: {combat.GrenadeCount} left");
                    smokeStep++; break;
                case 4:
                    if (smokeTime < 12) break;
                    SmokeLog($"after grenade: concrete destroyed {destruction.Blocks}, hp {combat.Hp}, downed {guards.Downed}, alert {GuardDirector.Phases[guards.AlertLevel].Name}");
                    smokeStep++; break;
                case 5:
                    // walk forward into the base for a few seconds, jumping once
                    Input.ActionPress("move_forward");
                    if (smokeTime > 12.2 && smokeTime < 12.3) Input.ActionPress("jump"); else Input.ActionRelease("jump");
                    smokeMaxFeet = Math.Max(smokeMaxFeet, player.Feet);
                    if (smokeTime < 16) break;
                    Input.ActionRelease("move_forward");
                    double moved = Math.Sqrt((player.X - level.ExtractionX) * (player.X - level.ExtractionX) + (player.Z - level.ExtractionZ) * (player.Z - level.ExtractionZ));
                    SmokeLog($"jump: highest feet {smokeMaxFeet:0.00} (floor 1.00), back on the ground {player.OnGround} at {player.Feet:0.00}");
                    SmokeLog($"walked: {moved:0.0}m from the pad, hp {combat.Hp}, alert {GuardDirector.Phases[guards.AlertLevel].Name}, state {state}");
                    smokeStep++; break;
                case 6:
                    if (smokeTime < 28) break;
                    int moving = guards.Guards.Count(g => g.Alive && g.Pace > .1);
                    SmokeLog($"after 12s more: guards {guards.Guards.Count}, alive {guards.Guards.Count(g => g.Alive)}, moving {moving}, hp {combat.Hp}, alert {GuardDirector.Phases[guards.AlertLevel].Name}, state {state}, fps {Engine.GetFramesPerSecond()}");
                    string dir = ProjectSettings.GlobalizePath("res://logs");
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "smoketest.txt"), smokeLog.ToString());
                    GetTree().Quit();
                    smokeStep++; break;
            }
        }

        void NewLevel()
        {
            uint seed = bench ? 4242u : (uint)rng.Next();
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
                "Click to deploy\nWASD move · Space jump · mouse aim · click fire · wheel fire mode · R reload · Q breach charge · G grenade · C crouch · Esc pause");
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (e is InputEventMouseButton wheel && wheel.Pressed && state == State.Playing
                && (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown))
                combat.CycleMode(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (state == State.Briefing || state == State.Paused) { Deploy(); GetViewport().SetInputAsHandled(); }
                else if (state == State.Playing) combat.TriggerPulled();   // single shot or start of a burst (auto is held)
                else if (state == State.Won) { levelNumber++; theme = PickTheme(); NewLevel(); }
                else if (state == State.RunOver) NewRun();
            }
            if (e is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (state == State.Initials) { TypeInitial(key); return; }
                if (key.Keycode == Key.E && (state == State.Won || state == State.Paused)) { EndRun(state == State.Paused ? "Run ended from the pause menu. The level in progress isn't scored." : "Run ended between levels."); return; }
                if ((key.Keycode == Key.Enter || key.Keycode == Key.KpEnter) && state == State.RunOver) { NewRun(); return; }
                if (key.Keycode == Key.Escape && state == State.Playing)
                {
                    state = State.Paused;
                    combat.Active = false;
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    hud.ShowOverlay("PAUSED", $"{theme.Name}  ·  level {levelNumber}\nRun total so far: {runTotal:N0} over {levelsCleared} level{(levelsCleared == 1 ? "" : "s")}.",
                        "Click to resume   ·   E: end run and submit score");
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
            combat.TriggerArmed = false;
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
            if (bench) { BenchStep(delta); return; }
            double dt = Math.Min(delta, .05);
            if (smoke && smokeStep < 7)
            {
                if (state != State.Playing) { SmokeLog($"run ended early: {state}"); smokeStep = 6; smokeTime = 99; }
                SmokeStep(dt);
            }
            if (state == State.Playing)
            {
                levelTime += dt;
                runTime += dt;
                double oldX = player.X, oldZ = player.Z;
                player.Step(dt);
                if (player.Landed > 7) sfx.Play("debris_thud", Mathf.Min(1f, (float)player.Landed / 20), .75f);
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

            // objective compass, as in the browser HUD: heading, an arrow toward the target, distance in metres (2 voxels = 1 m)
            double tx = hasIntel ? level.ExtractionX : level.IntelX, tz = hasIntel ? level.ExtractionZ : level.IntelZ;
            double dx = tx - player.X, dz = tz - player.Z, dist = Math.Sqrt(dx * dx + dz * dz);
            double fwdX = -Math.Sin(player.Yaw), fwdZ = -Math.Cos(player.Yaw), rightX = Math.Cos(player.Yaw), rightZ = -Math.Sin(player.Yaw);
            float arrowAngle = (float)Math.Atan2(dx * rightX + dz * rightZ, dx * fwdX + dz * fwdZ);
            hud.SetCompass((Math.PI - player.Yaw) * 180 / Math.PI, arrowAngle, (hasIntel ? "EXTRACT " : "INTEL ") + Math.Round(dist / 2) + " M");
            hud.SetLevel(levelNumber, hasIntel ? "02 · RETURN TO EXTRACTION" : "01 · RECOVER INTEL", theme.Name);
            hud.SetVitals(combat.Hp, combat.Hurt);
            string ammo = combat.Reload > 0 ? "RELOAD" : $"{combat.Magazine:00}/{combat.Reserve}";
            hud.SetKit($"{PlayerCombat.ModeNames[(int)combat.Mode]} (WHEEL)\n{ammo}  ·  {combat.Charges} CHG  ·  {combat.GrenadeCount} GRN (PRESS G)");
            hud.SetDebug($"{ScoreCalculator.FormatTime(levelTime)}   ·   {Version}   ·   {Engine.GetFramesPerSecond()} fps{(player.Crouched ? "   ·   CROUCHED" : "")}");
            hud.Status = state == State.Playing ? combat.ChargeStatus : null;
            hud.SetChargeMeter(state == State.Playing ? combat.ChargeProgress : -1);
            bool canPlant = state == State.Playing && combat.CanPlantHere();
            hud.SetPrompt(canPlant ? "Q" : null, "PLANT CHARGE");
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
                hud.ShowOverlay($"LEVEL {levelNumber} CLEAR", $"Intel secured in {ScoreCalculator.FormatTime(levelTime)}.\n\n{table}",
                    "Click: next base   ·   E: end run and submit score");
            else
                EndRun($"MISSION FAILED. {why ?? "You were hit before reaching extraction."}\n\n{table}");
        }

        // ---------- end of run, initials and the global board ----------

        void NewRun()
        {
            levelNumber = 1; levelsCleared = 0; runTotal = 0; runTime = 0; theme = Theme.All[0];
            NewLevel();
        }

        /// <summary>Ends the run (by choice or by death), banks the total and shows the global board.</summary>
        async void EndRun(string header)
        {
            state = State.RunOver;
            combat.Active = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            justAdded = null;
            runSummary = $"{header}\n\nRUN TOTAL {runTotal:N0}   ·   {levelsCleared} level{(levelsCleared == 1 ? "" : "s")} cleared   ·   {ScoreCalculator.FormatTime(runTime)}";
            hud.ShowOverlay("RUN OVER", runSummary + "\n\nGLOBAL TOP 10 · loading…", "");
            await leaderboard.Fetch();
            if (state != State.RunOver) return;
            if (leaderboard.Online && leaderboard.Qualifies(runTotal))
            {
                state = State.Initials;
                initials = "";
                ShowInitials();
            }
            else ShowBoard();
        }

        void ShowInitials()
        {
            string slots = string.Join(" ", Enumerable.Range(0, 3).Select(i => i < initials.Length ? initials[i].ToString() : "_"));
            hud.ShowOverlay("NEW HIGH SCORE", runSummary + "\n\nYou made the global top 10. Enter your initials:\n\n" + slots + "\n\n" + BoardText(),
                "Type up to 3 letters or digits   ·   Backspace to fix   ·   Enter to save");
        }

        async void TypeInitial(InputEventKey key)
        {
            if (key.Keycode == Key.Backspace && initials.Length > 0) initials = initials.Substring(0, initials.Length - 1);
            else if ((key.Keycode == Key.Enter || key.Keycode == Key.KpEnter) && initials.Length > 0)
            {
                state = State.RunOver;
                justAdded = new ScoreEntry { Name = initials, Score = runTotal, Time = runTime, Levels = levelsCleared };
                hud.ShowOverlay("RUN OVER", runSummary + "\n\nSaving…", "");
                bool saved = await leaderboard.Submit(justAdded);
                ShowBoard(saved ? null : "Couldn't reach the leaderboard: your score wasn't saved.");
                return;
            }
            else
            {
                char c = (char)key.Unicode;
                if (char.IsLetterOrDigit(c) && c < 128 && initials.Length < 3) initials += char.ToUpperInvariant(c);
            }
            ShowInitials();
        }

        string BoardText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(leaderboard.Online ? "GLOBAL TOP 10" : leaderboard.HasKey ? "GLOBAL TOP 10 · offline, showing last known" : "GLOBAL TOP 10 · not available in this build");
            for (int i = 0; i < Leaderboard.Size; i++)
            {
                var r = i < leaderboard.Board.Count ? leaderboard.Board[i] : null;
                bool mine = r != null && justAdded != null && r.Name == justAdded.Name && r.Score == justAdded.Score;
                string detail = r == null ? "" : r.Levels > 0 ? $"{r.Levels} LVL · {ScoreCalculator.FormatTime(r.Time)}" : "KIA";
                sb.AppendLine($"{(mine ? ">" : " ")}{i + 1,2}. {(r?.Name ?? "---"),-3}  {detail,-16} {(r == null ? "" : r.Score.ToString("N0")),9}");
            }
            return sb.ToString().TrimEnd();
        }

        void ShowBoard(string note = null)
        {
            hud.ShowOverlay("RUN OVER", runSummary + (note != null ? "\n" + note : "") + "\n\n" + BoardText(), "Click or Enter for a new run");
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

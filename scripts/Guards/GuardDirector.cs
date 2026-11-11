using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// The garrison and its alert system, ported from bunker-buster.js: four phases
    /// (Undetected, Caution, Evasion, Alert), flow-field pathing over the floor grid, noise,
    /// sight checks, the one-second report window, reinforcements, sentries at posts, and
    /// guards who wind up, then fire a visible round you can dodge.
    /// </summary>
    public partial class GuardDirector : Node3D
    {
        public const int Undetected = 0, Caution = 1, Evasion = 2, Alert = 3;
        public const int Garrison = 9, Reinforcements = 5, MaxGuards = 20;
        public const double ReportTime = 1, Windup = .6, BulletSpeed = 21, GuardSpacing = 1.15;
        public const double NoiseGun = 24, NoiseBlast = 46;
        const int X = VoxelWorld.SizeX, Z = VoxelWorld.SizeZ;

        public sealed class PhaseInfo
        {
            public string Name;
            public double Speed, Range, Fov, Hear, Time;
        }
        public static readonly PhaseInfo[] Phases =
        {
            new PhaseInfo { Name = "UNDETECTED", Speed = 1.15, Range = 15, Fov = .55, Hear = 4, Time = 0 },
            new PhaseInfo { Name = "CAUTION", Speed = 1.9, Range = 19, Fov = .35, Hear = 6, Time = 25 },
            new PhaseInfo { Name = "EVASION", Speed = 2.6, Range = 23, Fov = .15, Hear = 10, Time = 30 },
            new PhaseInfo { Name = "ALERT", Speed = 3.1, Range = 26, Fov = -.3, Hear = 14, Time = 12 },
        };

        public VoxelWorld World;
        public Destruction Destruction;
        public PlayerController Player;
        public Effects Fx;
        public Sfx Sound;
        public Action<string> Say;
        /// <summary>The player was hit by a round (damage).</summary>
        public Action<int> PlayerShot;

        public readonly List<Guard> Guards = new List<Guard>();
        readonly List<Bullet> bullets = new List<Bullet>();
        readonly Random rng = new Random();
        public int AlertLevel;
        public double AlertTimer;
        Vector2? lastKnown;
        bool reinforced;
        short[] chaseField;
        double chaseTimer, stepTimer;
        Theme theme;
        int levelNumber;

        sealed class Report
        {
            public double Timer, Timer0;
            public HashSet<Guard> Reporters;
            public int Level;
            public Vector2? LastKnown;
            public (double X, double Z, double Radius, string Kind)? Noise;
        }
        Report report;

        Texture2D sheet, downed, iconSpotted, iconHeard;
        BoxMesh tracerMesh;
        StandardMaterial3D tracerMat;

        public override void _Ready()
        {
            sheet = GD.Load<Texture2D>("res://assets/sprites/guard_sheet.png");
            downed = GD.Load<Texture2D>("res://assets/sprites/guard_downed.png");
            iconSpotted = GD.Load<Texture2D>("res://assets/sprites/icon_spotted.png");
            iconHeard = GD.Load<Texture2D>("res://assets/sprites/icon_heard.png");
            tracerMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, .85f, .5f), EmissionEnabled = true, Emission = new Color(1, .7f, .3f), EmissionEnergyMultiplier = 3 };
            tracerMesh = new BoxMesh { Size = new Vector3(.05f, .05f, .5f), Material = tracerMat };
        }

        double SightRule => theme.Sight * (1 + Math.Min(.2, (levelNumber - 1) * .025));

        // ---------- level start ----------

        public void Spawn(Level level, int levelNum)
        {
            foreach (var g in Guards) { g.Sprite?.QueueFree(); g.Icon?.QueueFree(); }
            Guards.Clear();
            foreach (var b in bullets) b.Mesh.QueueFree();
            bullets.Clear();
            theme = level.Theme;
            levelNumber = levelNum;
            var taken = new HashSet<int>();
            foreach (var s in level.Sentries)
            {
                var g = Make(s.X, s.Z, s.Cell, s.Yaw);
                g.HasPost = true; g.PostX = s.X; g.PostZ = s.Z; g.PostYaw = s.Yaw; g.Task = "post";
                taken.Add(s.Cell);
                Guards.Add(g);
            }
            int garrison = Garrison + level.Sentries.Count - 1 + theme.Garrison + Math.Min(6, (levelNum - 1) / 2);
            for (int tries = 0; Guards.Count < garrison && tries < 400; tries++)
            {
                int cell = rng.Next(MapPlan.Columns * MapPlan.Rows);
                int col = cell % MapPlan.Columns, row = cell / MapPlan.Columns;
                if (taken.Contains(cell) || row < 3 && Math.Abs(col - MapPlan.MidCol) < 3) continue;
                var (x, z) = MapPlan.CellCenter(cell);
                for (int k = 0; k < 8 && !World.CanStand(x, z, 4.4); k++)
                {
                    x += VoxelWorld.H3(cell, k, 1) % 5 - 2.0;
                    z += VoxelWorld.H3(cell, k, 2) % 5 - 2.0;
                }
                if (!World.CanStand(x, z, 4.4)) continue;
                taken.Add(cell);
                Guards.Add(Make(x, z, cell, rng.NextDouble() * 6.28));
            }
            AlertLevel = Undetected; AlertTimer = 0; lastKnown = null; reinforced = false; report = null;
            chaseField = null; chaseTimer = 0; stepTimer = 0;
            if (theme.StartOnCaution) { AlertLevel = Caution; AlertTimer = 60; }
        }

        Guard Make(double x, double z, int cell, double yaw)
        {
            var g = new Guard { X = x, Z = z, Cell = cell, Yaw = yaw, Wait = 1 + rng.NextDouble() * 3, Look = rng.NextDouble(), Pose = rng.Next(3) };
            g.Sprite = new Sprite3D
            {
                Texture = sheet, RegionEnabled = true, RegionRect = new Rect2(0, 0, 48, 89),
                Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                PixelSize = .049f, AlphaCut = SpriteBase3D.AlphaCutMode.Discard, Shaded = true,
            };
            g.Icon = new Sprite3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                PixelSize = .045f, Visible = false, NoDepthTest = true, AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            };
            AddChild(g.Sprite);
            AddChild(g.Icon);
            return g;
        }

        // ---------- pathing ----------

        bool Walkable(int x, int z) => x > 0 && z > 0 && x < X - 1 && z < Z - 1
            && World.Tile(x, 1, z) == 0 && World.Tile(x, 2, z) == 0 && World.Tile(x, 3, z) == 0 && World.Tile(x, 4, z) == 0;

        /// <summary>Breadth-first walking distance from (x, z) to every reachable floor cell (-1 = unreachable).</summary>
        public short[] FlowField(double x, double z, int radius = 9999)
        {
            var field = new short[X * Z];
            Array.Fill(field, (short)-1);
            int sx = (int)Math.Floor(x), sz = (int)Math.Floor(z);
            var queue = new List<int>();
            for (int r = 0; r < 4 && queue.Count == 0; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (Walkable(sx + dx, sz + dz) && field[(sz + dz) * X + sx + dx] < 0)
                        {
                            field[(sz + dz) * X + sx + dx] = 0;
                            queue.Add((sz + dz) * X + sx + dx);
                        }
            for (int q = 0; q < queue.Count; q++)
            {
                int c = queue[q], cx = c % X, cz = c / X, d = field[c];
                if (d >= radius) continue;
                Visit(cx + 1, cz); Visit(cx - 1, cz); Visit(cx, cz + 1); Visit(cx, cz - 1);
                void Visit(int nx, int nz)
                {
                    if (Walkable(nx, nz) && field[nz * X + nx] < 0) { field[nz * X + nx] = (short)(d + 1); queue.Add(nz * X + nx); }
                }
            }
            return field;
        }

        static short FieldAt(short[] f, double x, double z)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            if (ix < 0 || iz < 0 || ix >= X || iz >= Z) return -1;
            return f[iz * X + ix];
        }

        void Move(Guard g, double dx, double dz, double top = 4.3)
        {
            if (World.CanStand(g.X + dx, g.Z, top)) g.X += dx;
            if (World.CanStand(g.X, g.Z + dz, top)) g.Z += dz;
        }

        /// <summary>One step downhill on a field. Returns true once arrived (or stuck).</summary>
        bool FollowField(Guard g, short[] f, double speed, double dt, int arrive = 2)
        {
            int cx = (int)Math.Floor(g.X), cz = (int)Math.Floor(g.Z);
            int here = f[cz * X + cx];
            if (here < 0 || here <= arrive) return true;
            double bx = 0, bz = 0; bool found = false; int bd = here;
            foreach (var (nx, nz) in new[] { (cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1) })
            {
                int d = f[nz * X + nx];
                if (d >= 0 && d < bd) { bd = d; bx = nx + .5; bz = nz + .5; found = true; }
            }
            if (!found) return true;
            double tx = bx - g.X, tz = bz - g.Z, tl = Math.Sqrt(tx * tx + tz * tz);
            if (tl == 0) tl = 1;
            double want = Math.Atan2(tx, tz);
            g.Yaw += Math.Atan2(Math.Sin(want - g.Yaw), Math.Cos(want - g.Yaw)) * Math.Min(1, dt * 8);
            Move(g, tx / tl * dt * speed, tz / tl * dt * speed);
            return false;
        }

        // ---------- alert ----------

        void SetPhase(int level)
        {
            if (level == AlertLevel) { AlertTimer = Phases[level].Time; return; }
            bool rising = level > AlertLevel;
            AlertLevel = level; AlertTimer = Phases[level].Time;
            foreach (var g in Guards) if (g.Alive) { g.Task = "idle"; g.Wait = rising ? 0 : .5 + rng.NextDouble(); g.Goal = null; }
            if (level == Alert)
            {
                if (report == null)
                {
                    Say?.Invoke("!! ALERT !!  REINFORCEMENTS INBOUND");
                    Sound?.Play("alarm", .7f);
                    if (!reinforced) { reinforced = true; SpawnReinforcements(); }
                }
                else Say?.Invoke("SPOTTED · SILENCE HIM BEFORE HE REPORTS");
            }
            else if (level == Evasion) Say?.Invoke(rising ? "EVASION · THEY KNOW YOU'RE HERE" : "EVASION · THEY LOST YOU");
            else if (level == Caution) Say?.Invoke(rising ? "CAUTION · SOMETHING WAS HEARD" : "CAUTION · STAND DOWN SOON");
            else { Say?.Invoke("UNDETECTED"); reinforced = false; }
        }

        public void RaiseTo(int level, double? x = null, double? z = null)
        {
            if (x.HasValue) lastKnown = new Vector2((float)x.Value, (float)z.Value);
            if (level >= AlertLevel) SetPhase(level);
        }

        void SpottedBy(Guard g)
        {
            if (AlertLevel < Alert)
            {
                report = new Report { Timer = ReportTime, Reporters = new HashSet<Guard> { g }, Level = AlertLevel, Timer0 = AlertTimer, LastKnown = lastKnown };
                lastKnown = new Vector2((float)Player.X, (float)Player.Z);
                SetPhase(Alert);
                return;
            }
            lastKnown = new Vector2((float)Player.X, (float)Player.Z);
            AlertTimer = Phases[Alert].Time;
            report?.Reporters.Add(g);
        }

        void UpdateReport(double dt)
        {
            if (report == null) return;
            if (!report.Reporters.Any(g => g.Alive))
            {
                // every witness died before the word got out: back to how things were
                var r = report; report = null;
                AlertLevel = r.Level; AlertTimer = r.Level > 0 ? r.Timer0 : 0; lastKnown = r.LastKnown;
                foreach (var g in Guards) if (g.Alive) { g.Task = "idle"; g.Wait = .4 + rng.NextDouble(); g.Goal = null; g.Saw = false; }
                Say?.Invoke("REPORTER SILENCED · ALERT CALLED OFF");
                if (r.Noise.HasValue)
                {
                    var n = r.Noise.Value;   // shots fired still get heard
                    MakeNoise(n.X, n.Z, n.Radius, n.Kind);
                }
                return;
            }
            report.Timer -= dt;
            if (report.Timer <= 0)
            {
                report = null;
                Say?.Invoke("!! ALERT !!  REINFORCEMENTS INBOUND");
                Sound?.Play("alarm", .7f);
                if (!reinforced) { reinforced = true; SpawnReinforcements(); }
            }
        }

        void SpawnReinforcements()
        {
            int living = Guards.Count(g => g.Alive);
            int count = Math.Min(Reinforcements, MaxGuards - living);
            if (count <= 0) return;
            var pf = FlowField(Player.X, Player.Z);
            var ring = new List<(int Cell, double X, double Z, int D)>();
            for (int cell = 0; cell < MapPlan.Columns * MapPlan.Rows; cell++)
            {
                int col = cell % MapPlan.Columns, row = cell / MapPlan.Columns;
                if (col != 0 && col != MapPlan.Columns - 1 && row != 0 && row != MapPlan.Rows - 1) continue;
                var (cx, cz) = MapPlan.CellCenter(cell);
                int d = FieldAt(pf, cx, cz);
                double dx = cx - Player.X, dz = cz - Player.Z, dl = Math.Sqrt(dx * dx + dz * dz);
                var eye = new Vector3((float)Player.X, (float)Player.Eye, (float)Player.Z);
                bool visible = !Destruction.Trace(eye, new Vector3((float)(dx / dl), (float)((3 - Player.Eye) / dl), (float)(dz / dl)), dl - .3).Hit;
                if (d < 0 || visible) continue;
                ring.Add((cell, cx, cz, d));
            }
            ring.Sort((a, b) => b.D.CompareTo(a.D));
            var pool = ring.Where(r => r.D >= 28).ToList();
            var picks = (pool.Count >= count ? pool : ring).Take(count * 2).ToList();
            for (int i = picks.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (picks[i], picks[j]) = (picks[j], picks[i]); }
            foreach (var r in picks.Take(count))
            {
                var g = Make(r.X + rng.NextDouble() - .5, r.Z + rng.NextDouble() - .5, r.Cell, 0);
                g.Wait = 0; g.Shot = 2;
                Guards.Add(g);
            }
        }

        /// <summary>A gunshot, blast or footstep: listeners within walking distance go and look.</summary>
        public void MakeNoise(double x, double z, double radius, string kind)
        {
            var f = FlowField(x, z, (int)radius);
            if (report != null && kind != "step") report.Noise = (x, z, radius, kind);
            var listeners = Guards.Where(g => g.Alive && FieldAt(f, g.X, g.Z) >= 0).ToList();
            if (listeners.Count == 0) return;
            if (kind == "step")
            {
                if (AlertLevel == Evasion) { lastKnown = new Vector2((float)x, (float)z); AlertTimer = Phases[Evasion].Time; }
            }
            else
            {
                Sound?.Play("noise_heard", .35f);
                if (AlertLevel == Alert) lastKnown = new Vector2((float)x, (float)z);
                else RaiseTo(AlertLevel == Evasion ? Evasion : Caution, x, z);
            }
            if (AlertLevel < Alert)
                foreach (var g in listeners)
                {
                    g.Task = "investigate"; g.Field = f; g.Goal = new Vector2((float)x, (float)z); g.Wait = 0;
                    if (!g.Saw) g.Query = 1.4;
                }
        }

        bool CanSee(Guard g, double dx, double dz, double dist)
        {
            var P = Phases[AlertLevel];
            double range = P.Range * (Player.Crouched && AlertLevel < Alert ? .62 : 1) * (g.HasPost && g.Task == "post" ? 1.3 : 1) * SightRule;
            if (dist > range || dist < .01) return false;
            double facing = Math.Cos(Math.Atan2(dx, dz) - g.Yaw);
            if (facing < P.Fov && dist > 2.2) return false;
            var from = new Vector3((float)g.X, 3.75f, (float)g.Z);
            var dir = new Vector3((float)(dx / dist), (float)((Player.Eye - 3.75) / dist), (float)(dz / dist));
            return !Destruction.Trace(from, dir, dist - .12).Hit;
        }

        Vector2 PickRoom(Guard g, bool sweep)
        {
            if (!sweep)
            {
                int col = g.Cell % MapPlan.Columns, row = g.Cell / MapPlan.Columns;
                int c = Math.Clamp(col + rng.Next(5) - 2, 0, MapPlan.Columns - 1);
                int r = Math.Clamp(row + rng.Next(5) - 2, 0, MapPlan.Rows - 1);
                var (px, pz) = MapPlan.CellCenter(r * MapPlan.Columns + c);
                return new Vector2((float)px, (float)pz);
            }
            var claimed = Guards.Where(o => o != g && o.Goal.HasValue).Select(o => o.Goal.Value).ToList();
            Vector2 best = default; double bestScore = -1e9;
            for (int k = 0; k < 12; k++)
            {
                var (cx, cz) = MapPlan.CellCenter(rng.Next(MapPlan.Columns * MapPlan.Rows));
                var c = new Vector2((float)cx, (float)cz);
                double near = lastKnown.HasValue ? c.DistanceTo(lastKnown.Value) : 0;
                double spread = 99;
                foreach (var o in claimed) spread = Math.Min(spread, c.DistanceTo(o));
                double score = Math.Min(spread, 24) * 1.2 - near * .6 + rng.NextDouble() * 6;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        void Think(Guard g, double dt, bool sees, double heading, double dist)
        {
            var P = Phases[AlertLevel];
            if (AlertLevel == Alert)
            {
                if (sees)
                {
                    g.Yaw = heading;
                    if (dist > 9) Move(g, Math.Sin(heading) * dt * P.Speed * .6, Math.Cos(heading) * dt * P.Speed * .6);
                    return;
                }
                if (chaseField != null && !FollowField(g, chaseField, P.Speed, dt, 1)) return;
                g.Yaw += dt * 2.2;   // reached the last known spot: scan
                return;
            }
            if (g.HasPost && AlertLevel == Undetected)
            {
                // sentry: walk back to the post if pulled away, then slowly sweep the arc in front of it
                if (Math.Sqrt((g.X - g.PostX) * (g.X - g.PostX) + (g.Z - g.PostZ) * (g.Z - g.PostZ)) > 1)
                {
                    g.PostField ??= FlowField(g.PostX, g.PostZ);
                    if (!FollowField(g, g.PostField, P.Speed, dt, 0)) return;
                }
                g.Task = "post"; g.Scan += dt;
                g.Yaw = g.PostYaw + Math.Sin(g.Scan * .45) * .85;
                return;
            }
            if (g.Task == "investigate" && g.Field != null)
            {
                if (!FollowField(g, g.Field, P.Speed + .6, dt)) return;
                g.Task = "search"; g.Wait = AlertLevel == Evasion ? 2 : 4 + rng.NextDouble() * 2; g.Field = null;
            }
            if (g.Task == "walk" && g.Field != null)
            {
                if (!FollowField(g, g.Field, P.Speed, dt)) return;
                g.Task = "search"; g.Wait = AlertLevel == Evasion ? 1.5 + rng.NextDouble() : 2 + rng.NextDouble() * 3; g.Field = null;
            }
            g.Wait -= dt;
            g.Yaw += dt * (AlertLevel == Undetected ? .5 : 1.6) * Math.Sin(g.Wait * 1.7 + g.X);
            if (g.Wait > 0) return;
            var target = PickRoom(g, AlertLevel == Evasion);
            var f = FlowField(target.X, target.Y);
            if (FieldAt(f, g.X, g.Z) < 0) { g.Wait = 1; return; }
            g.Goal = target; g.Field = f; g.Task = "walk";
        }

        void FireBullet(Guard g)
        {
            g.Muzzle = .12;
            var from = new Vector3((float)(g.X + Math.Sin(g.Yaw) * .7), 3.25f, (float)(g.Z + Math.Cos(g.Yaw) * .7));
            Sound?.PlayAt("guard_shot", from, .8f);
            double tx = Player.X, tz = Player.Z, ty = Player.Eye - .7;
            // standing still gives the old odds; moving sideways can dodge the round
            if (rng.NextDouble() > (Player.Crouched ? .37 : .64))
            {
                int side = rng.NextDouble() < .5 ? -1 : 1;
                double off = .9 + rng.NextDouble() * .7;
                tx += Math.Cos(g.Yaw) * off * side; tz -= Math.Sin(g.Yaw) * off * side; ty += (rng.NextDouble() - .3) * .8;
            }
            var d = new Vector3((float)tx, (float)ty, (float)tz) - from;
            var b = new Bullet { P = from, V = d.Normalized() * (float)BulletSpeed };
            b.Mesh = new MeshInstance3D { Mesh = tracerMesh, Position = from, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(b.Mesh);
            b.Mesh.LookAt(from + b.V, Vector3.Up);
            bullets.Add(b);
        }

        // ---------- per frame ----------

        public void Step(double dt)
        {
            if (Player == null) return;
            // footsteps: running is loud, crouch-walking is silent
            stepTimer -= dt;
            if (Player.Travel > dt * 2 && !Player.Crouched && stepTimer <= 0)
            {
                stepTimer = .45;
                double r = Phases[AlertLevel].Hear * theme.Hear;
                if (Guards.Any(g => g.Alive && Math.Abs(g.X - Player.X) < r + 2 && Math.Abs(g.Z - Player.Z) < r + 2))
                    MakeNoise(Player.X, Player.Z, r, "step");
            }
            if (AlertLevel == Alert)
            {
                chaseTimer -= dt;
                if (chaseTimer <= 0 && lastKnown.HasValue) { chaseTimer = .5; chaseField = FlowField(lastKnown.Value.X, lastKnown.Value.Y); }
            }
            if (AlertLevel > Undetected)
            {
                bool seen = Guards.Any(g => g.Alive && g.Saw);
                if (!(AlertLevel == Alert && seen)) AlertTimer -= dt;
                if (AlertTimer <= 0) SetPhase(AlertLevel - 1);
            }

            foreach (var g in Guards.ToList())
            {
                if (!g.Alive) { g.Fall = Math.Min(1, g.Fall + dt * 3.4); continue; }
                g.Impact = Math.Max(0, g.Impact - dt);
                double oldX = g.X, oldZ = g.Z;
                double dx = Player.X - g.X, dz = Player.Z - g.Z, dist = Math.Sqrt(dx * dx + dz * dz);
                double heading = Math.Atan2(dx, dz);
                bool sees = CanSee(g, dx, dz, dist);
                g.Spot = Math.Max(0, g.Spot - dt);
                g.Query = Math.Max(0, g.Query - dt);
                if (sees)
                {
                    if (!g.Saw)
                    {
                        // spotted: "!" over the head, a sting, and a beat before the first shot
                        g.Spot = 1.1;
                        g.Shot = Math.Max(g.Shot, .45);
                        Sound?.PlayAt("alert_spotted", new Vector3((float)g.X, 3.5f, (float)g.Z));
                    }
                    SpottedBy(g);
                }
                g.Saw = sees;
                Think(g, dt, sees, heading, dist);
                double stepDistance = Math.Sqrt((g.X - oldX) * (g.X - oldX) + (g.Z - oldZ) * (g.Z - oldZ));
                g.Gait += stepDistance * 7;
                g.Pace += (Math.Min(1, stepDistance / (dt > 0 ? dt : 1)) - g.Pace) * Math.Min(1, dt * 12);
                g.Muzzle = Math.Max(0, g.Muzzle - dt);
                g.Shot = Math.Max(0, g.Shot - dt);
                if (g.Aim > 0)
                {
                    g.Yaw = heading;
                    g.Aim = Math.Max(0, g.Aim - dt);
                    if (g.Aim == 0)
                    {
                        g.Shot = 1.2 + rng.NextDouble() * .55;
                        if (sees) FireBullet(g);
                    }
                }
                else if (sees && g.Shot == 0)
                {
                    g.Aim = Windup;   // raise rifle, then fire
                    Sound?.PlayAt("rifle_raise", new Vector3((float)g.X, 3.5f, (float)g.Z), .6f);
                }
            }
            Separate();
            UpdateReport(dt);
            StepBullets(dt);
            UpdateSprites();
        }

        void Separate()
        {
            var live = Guards.Where(g => g.Alive).ToList();
            for (int i = 0; i < live.Count; i++)
                for (int j = i + 1; j < live.Count; j++)
                {
                    Guard a = live[i], b = live[j];
                    double dx = b.X - a.X, dz = b.Z - a.Z, d = Math.Sqrt(dx * dx + dz * dz);
                    if (d >= GuardSpacing) continue;
                    if (d < 1e-3) { double t = rng.NextDouble() * Math.PI * 2; dx = Math.Sin(t); dz = Math.Cos(t); d = 1; }
                    else { dx /= d; dz /= d; }
                    double push = (GuardSpacing - Math.Min(d, GuardSpacing)) * .5;
                    Move(a, -dx * push, -dz * push);
                    Move(b, dx * push, dz * push);
                }
        }

        void StepBullets(double dt)
        {
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i];
                for (int k = 0; k < 3 && b.Life > 0; k++)
                {
                    b.P += b.V * (float)(dt / 3);
                    double px = b.P.X - Player.X, pz = b.P.Z - Player.Z;
                    if (Math.Sqrt(px * px + pz * pz) < .55 && b.P.Y > 1 && b.P.Y < Player.Eye + .35)
                    {
                        b.Life = 0;
                        PlayerShot?.Invoke(12);
                    }
                    else if (World.Tile((int)Math.Floor(b.P.X), (int)Math.Floor(b.P.Y), (int)Math.Floor(b.P.Z)) != 0)
                    {
                        b.Life = 0;
                        Fx?.Sparks(b.P - b.V * .01f, 6);
                    }
                }
                b.Life -= dt;
                b.Mesh.Position = b.P;
                if (b.Life <= 0) { b.Mesh.QueueFree(); bullets.RemoveAt(i); }
            }
        }

        // ---------- player shooting at guards ----------

        /// <summary>The nearest living guard along a shot from the eye, closer than maxT (JS shoot()).</summary>
        public Guard HitScan(Vector3 eye, Vector3 dir, double maxT, out double t)
        {
            Guard target = null;
            t = maxT;
            double dx = dir.X, dy = dir.Y, dz = dir.Z;
            foreach (var g in Guards)
            {
                if (!g.Alive) continue;
                double gx = g.X - eye.X, gz = g.Z - eye.Z;
                double tt = (gx * dx + gz * dz) / (dx * dx + dz * dz);
                double y = eye.Y + tt * dy;
                double ox = gx - tt * dx, oz = gz - tt * dz;
                if (tt > 0 && tt < t && y > 1 && y < 4.25 && Math.Sqrt(ox * ox + oz * oz) < .54) { t = tt; target = g; }
            }
            return target;
        }

        /// <summary>A rifle hit: wounded guards turn toward you and the base goes to Evasion.</summary>
        public void Shoot(Guard g)
        {
            g.Hp--; g.Impact = .19;
            Say?.Invoke(g.Alive ? "HIT" : "HOSTILE DOWN");
            if (g.Alive) { g.Yaw = Math.Atan2(Player.X - g.X, Player.Z - g.Z); RaiseTo(Evasion, Player.X, Player.Z); }
        }

        /// <summary>Blast damage to soldiers within radius r of the centre.</summary>
        public void Blast(Vector3 c, double r)
        {
            foreach (var g in Guards)
            {
                if (!g.Alive) continue;
                double d = Math.Sqrt((g.X - c.X) * (g.X - c.X) + (2.5 - c.Y) * (2.5 - c.Y) + (g.Z - c.Z) * (g.Z - c.Z));
                if (d < r) { g.Hp = Math.Max(0, g.Hp - (d < r * .67 ? 2 : 1)); g.Impact = .25; }
            }
        }

        /// <summary>Falling debris at p hurts a guard under it. Returns true if one was hit.</summary>
        public bool DebrisHits(Vector3 p)
        {
            foreach (var g in Guards)
                if (g.Alive && Math.Abs(p.X - g.X) < .7 && Math.Abs(p.Z - g.Z) < .7) { g.Hp--; g.Impact = .2; return true; }
            return false;
        }

        public bool NearAnyGuard(Vector3 p, double r) =>
            Guards.Any(g => g.Alive && Math.Sqrt((p.X - g.X) * (p.X - g.X) + (p.Z - g.Z) * (p.Z - g.Z)) < r);

        public int Downed => Guards.Count(g => !g.Alive);

        // ---------- sprites ----------

        void UpdateSprites()
        {
            var cam = Player.Camera;
            if (cam == null) return;
            Vector3 camRight = cam.GlobalTransform.Basis.X;
            foreach (var g in Guards)
            {
                var s = g.Sprite;
                if (!g.Alive)
                {
                    if (s.Texture != downed)
                    {
                        s.Texture = downed;
                        s.RegionEnabled = false;
                        s.FlipH = g.Pose == 1;
                    }
                    // drop from standing to lying as Fall goes 0 -> 1
                    float h = 23 * .049f;
                    s.Position = new Vector3((float)g.X, 1 + h / 2 + (float)(1 - g.Fall) * 1.5f, (float)g.Z);
                    g.Icon.Visible = false;
                    continue;
                }
                double fx = Math.Sin(g.Yaw), fz = Math.Cos(g.Yaw);
                double tx = Player.X - g.X, tz = Player.Z - g.Z, tl = Math.Sqrt(tx * tx + tz * tz);
                if (tl == 0) tl = 1;
                double toward = (fx * tx + fz * tz) / tl;
                int row, col;
                bool flip = false;
                if (toward > .72) row = 0;
                else if (toward < -.72) row = 1;
                else { row = 2; flip = fx * camRight.X + fz * camRight.Z < 0; }
                if (g.Muzzle > 0) col = 5;
                else if (g.Aim > 0) col = 4;
                else col = g.Pace > .15 ? (int)(g.Gait % 4) : 0;
                s.RegionRect = new Rect2(col * 48, row * 89, 48, 89);
                s.FlipH = flip;
                s.Modulate = g.Impact > 0 ? new Color(1.6f, .7f, .7f) : Colors.White;
                s.Position = new Vector3((float)g.X, 1 + 89 * .049f / 2, (float)g.Z);
                if (g.Spot > 0) { g.Icon.Texture = iconSpotted; g.Icon.Visible = true; }
                else if (g.Query > 0) { g.Icon.Texture = iconHeard; g.Icon.Visible = true; }
                else g.Icon.Visible = false;
                g.Icon.Position = s.Position + Vector3.Up * 3;
            }
        }
    }
}

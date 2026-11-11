using System;
using System.Collections.Generic;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// The player's kit and health, ported from shoot(), reloadRifle(), breach(), updateCharge(),
    /// explode(), the fuel-drum chain and the grenade code in bunker-buster.js.
    /// 24-round magazine (144 reserve), breach charges that blow out a ball of wall, three grenades
    /// per level, fuel drums that go off and chain.
    /// </summary>
    public partial class PlayerCombat : Node3D
    {
        public const int MagazineSize = 24, Grenades = 3;
        public const double FireCooldown = .21, ReloadTime = 1.15, PlantTime = .55, FuseTime = 1.65, PlantRange = 4.5;
        public const double GrenadeFuse = 2.2, GrenadeGravity = 16;

        public VoxelWorld World;
        public PlayerController Player;
        public Destruction Destruction;
        public GuardDirector Guards;
        public Effects Fx;
        public Sfx Sound;
        public ViewModel Gun;
        public Action<string> Say;
        /// <summary>The player has died; the text says how.</summary>
        public Action<string> Died;
        /// <summary>Camera shake request (0-1).</summary>
        public Action<float> Shake;

        public int Hp = 100, Magazine = MagazineSize, Reserve = 144, Charges = 10, GrenadeCount = Grenades;
        public double Cooldown, Reload, Flash, Hurt, ThrowCool;
        public bool Active;
        readonly Random rng = new Random();

        sealed class Charge { public Vector3 P; public float Dx, Dz; public double Plant, Fuse; public Sprite3D Sprite; }
        Charge charge;
        sealed class Grenade { public Vector3 P, V; public double Fuse, Clink; public MeshInstance3D Mesh; public bool Dead; }
        readonly List<Grenade> grenades = new List<Grenade>();
        readonly List<(int X, int Z, double T)> pendingBlasts = new List<(int, int, double)>();
        SphereMesh grenadeMesh;
        Texture2D chargeTexture;

        public override void _Ready()
        {
            grenadeMesh = new SphereMesh { Radius = .14f, Height = .28f, Material = new StandardMaterial3D { AlbedoColor = new Color(.25f, .3f, .2f), Roughness = .6f } };
            chargeTexture = GD.Load<Texture2D>("res://assets/sprites/breach_charge.png");
        }

        public void Reset(Theme theme)
        {
            Hp = 100; Magazine = MagazineSize; Reserve = 144; Charges = theme.Charges; GrenadeCount = Grenades;
            Cooldown = Reload = Flash = Hurt = ThrowCool = 0;
            if (charge != null) { charge.Sprite.QueueFree(); charge = null; }
            foreach (var g in grenades) g.Mesh.QueueFree();
            grenades.Clear();
            pendingBlasts.Clear();
        }

        Vector3 Eye => new Vector3((float)Player.X, (float)Player.Eye, (float)Player.Z);

        // ---------- rifle ----------

        public void Fire()
        {
            if (!Active || Cooldown > 0 || Reload > 0 || charge != null && charge.Plant > 0) return;
            if (Magazine == 0) { StartReload(); return; }
            Magazine--; Cooldown = FireCooldown; Flash = .075;
            Gun?.OnShot();
            Sound?.Play("rifle_shot", .9f, .95f + (float)rng.NextDouble() * .1f);
            Guards.MakeNoise(Player.X, Player.Z, GuardDirector.NoiseGun, "gun");
            Vector3 dir = Player.LookDirection, eye = Eye;
            var hit = Destruction.Trace(eye, dir, 60);
            var target = Guards.HitScan(eye, dir, hit.T, out _);
            if (target != null) { Guards.Shoot(target); return; }
            if (!hit.Hit) return;
            Vector3 at = eye + dir * (float)hit.T;
            if (hit.Material == 2 || hit.Material == 7)
            {
                Destruction.ShootOut(hit.X, hit.Y, hit.Z);
                Fx.Burst(at - dir * .04f, 10);
                Fx.Sparks(at - dir * .04f, 3);
            }
            else if (hit.Material == 15)
            {
                DetonateDrum(hit.X, hit.Z, .04);
                Fx.Sparks(at, 6);
            }
            else
            {
                // steel, reinforced concrete, crates, drums, sandbags: the round stops
                Fx.Sparks(at - dir * .04f, hit.Material == 8 ? 0 : 5);
                if (hit.Material != 8) Fx.Dust(at - dir * .04f, .6f, 1, .5f);
            }
        }

        public void StartReload()
        {
            if (Reload == 0 && Magazine < MagazineSize && Reserve > 0)
            {
                Reload = ReloadTime;
                Sound?.Play("reload", .7f);
            }
        }

        // ---------- breach charge ----------

        static bool Breachable(byte m) => m == 2 || m == 10 || m == 11;

        public void PlantCharge()
        {
            if (!Active || charge != null) return;
            if (Charges == 0) { Say?.Invoke("NO BREACH CHARGES"); return; }
            Vector3 dir = Player.LookDirection, eye = Eye;
            var hit = Destruction.Trace(eye, dir, PlantRange);
            if (!Breachable(hit.Material))
            {
                Say?.Invoke(hit.Material == 3 ? "OUTER WALL IS TOO THICK · AIM AT AN INNER WALL" : "MOVE CLOSE AND AIM AT A WALL OR GATE");
                return;
            }
            var p = eye + dir * (float)hit.T;
            charge = new Charge { P = p, Dx = dir.X, Dz = dir.Z, Plant = PlantTime, Fuse = FuseTime };
            charge.Sprite = new Sprite3D
            {
                Texture = chargeTexture, PixelSize = .03f, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = p - dir * .06f, Visible = false,
            };
            AddChild(charge.Sprite);
            Sound?.Play("charge_plant", .5f, .8f);
        }

        void StepCharge(double dt)
        {
            if (charge == null) return;
            if (charge.Plant > 0)
            {
                double dx = Player.X - charge.P.X, dz = Player.Z - charge.P.Z;
                if (Math.Sqrt(dx * dx + dz * dz) > PlantRange + .35)
                {
                    charge.Sprite.QueueFree(); charge = null; Say?.Invoke("PLANT CANCELLED"); return;
                }
                charge.Plant = Math.Max(0, charge.Plant - dt);
                if (charge.Plant == 0) { Charges--; charge.Sprite.Visible = true; Sound?.Play("charge_plant", .7f); }
                return;
            }
            double previous = charge.Fuse;
            charge.Fuse -= dt;
            if (charge.Fuse > 0)
            {
                if (Math.Ceiling(previous * 3) != Math.Ceiling(charge.Fuse * 3)) Sound?.PlayAt("fuse_beep", charge.P, .5f);
                return;
            }
            var c = charge;
            charge = null;
            c.Sprite.QueueFree();
            Explode(c.P, c.Dx, c.Dz, new Vector3(c.P.X + c.Dx * 1.7f, c.P.Y - .4f, c.P.Z + c.Dz * 1.7f), 3.7, 8, 110, 6,
                "The breach charge got you. Back off after planting.");
        }

        // ---------- grenades ----------

        public void ThrowGrenade()
        {
            if (!Active || ThrowCool > 0) return;
            if (GrenadeCount == 0) { Say?.Invoke("NO GRENADES LEFT"); return; }
            GrenadeCount--; ThrowCool = .7;
            var dir = Player.LookDirection;
            double sx = Player.X + dir.X * .5, sz = Player.Z + dir.Z * .5;
            bool clear = World.CanStand(sx, sz, Player.Eye);
            var g = new Grenade
            {
                P = new Vector3((float)(clear ? sx : Player.X), (float)Player.Eye - .35f, (float)(clear ? sz : Player.Z)),
                V = new Vector3(dir.X * 12.5f, dir.Y * 12.5f + 4, dir.Z * 12.5f),
                Fuse = GrenadeFuse,
            };
            g.Mesh = new MeshInstance3D { Mesh = grenadeMesh, Position = g.P };
            AddChild(g.Mesh);
            grenades.Add(g);
            Say?.Invoke("GRENADE OUT");
        }

        bool Solid(float x, float y, float z) => World.Tile((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z)) != 0;

        void StepGrenades(double dt)
        {
            ThrowCool = Math.Max(0, ThrowCool - dt);
            foreach (var g in grenades)
            {
                g.Fuse -= dt; g.Clink -= dt;
                float h = (float)dt / 4;
                for (int k = 0; k < 4; k++)
                {
                    g.V.Y -= (float)GrenadeGravity * h;
                    // one axis at a time, bouncing off whatever face it hits and losing energy
                    float nx = g.P.X + g.V.X * h;
                    if (Solid(nx, g.P.Y, g.P.Z)) { g.V.X *= -.45f; g.V.Z *= .8f; Clink(g); } else g.P.X = nx;
                    float nz = g.P.Z + g.V.Z * h;
                    if (Solid(g.P.X, g.P.Y, nz)) { g.V.Z *= -.45f; g.V.X *= .8f; Clink(g); } else g.P.Z = nz;
                    float ny = g.P.Y + g.V.Y * h;
                    if (Solid(g.P.X, ny, g.P.Z))
                    {
                        if (g.V.Y < 0) { g.V.X *= .62f; g.V.Z *= .62f; }
                        g.V.Y = Math.Abs(g.V.Y) > 1.5f ? -g.V.Y * .38f : 0;
                        Clink(g);
                    }
                    else g.P.Y = ny;
                }
                g.Mesh.Position = g.P;
                if (g.Fuse <= 0)
                {
                    g.Dead = true;
                    // centre the blast at waist height so a grenade against a wall bites a real hole
                    Explode(g.P + Vector3.Up * .3f, 0, 0, new Vector3(g.P.X, Math.Max(g.P.Y + .3f, 2.3f), g.P.Z), 3.1, 6.5, 85, 4.6,
                        "Your own grenade got you. Throw it further.");
                    if (!Active) break;
                }
            }
            for (int i = grenades.Count - 1; i >= 0; i--)
                if (grenades[i].Dead) { grenades[i].Mesh.QueueFree(); grenades.RemoveAt(i); }
        }

        void Clink(Grenade g)
        {
            if (g.V.Length() < 1.2f || g.Clink > 0) return;
            g.Clink = .12;
            Sound?.PlayAt("fuse_beep", g.P, .25f, 1.8f);
        }

        // ---------- explosions ----------

        /// <summary>
        /// One explosion, whatever set it off. `at` is where the flash comes from, `c` the centre of
        /// damage; carve = radius of wall blown out; reach/damage hurt the player (halved through a
        /// wall); guardR downs or wounds soldiers.
        /// </summary>
        void Explode(Vector3 at, float dx, float dz, Vector3 c, double carve, double reach, double damage, double guardR, string cause)
        {
            ArmDrums(c, Math.Max(carve, guardR * .6));
            int removed = 0, before = 0;
            if (carve > 0)
            {
                before = Destruction.CountMaterial(10, c, carve);
                removed = Destruction.Carve(c, carve, p => { if (rng.NextDouble() < .1) Fx.Burst(p, 2); });
            }
            Fx.Blast(at - new Vector3(dx, 0, dz) * .35f, carve > 0 ? 1.2f : .9f);
            Sound?.PlayAt("explosion", c, 1);
            double pdx = Player.X - c.X, pdz = Player.Z - c.Z;
            Shake?.Invoke(carve > 0 ? .55f : (float)(.4 * Math.Max(0, 1 - Math.Sqrt(pdx * pdx + pdz * pdz) / 30)));
            var body = new Vector3((float)Player.X, (float)Player.Eye - 1.4f, (float)Player.Z);
            double pd = body.DistanceTo(c);
            if (pd < reach)
            {
                double dmg = Math.Round(damage * (1 - pd / reach));
                if (pd > .01 && Destruction.Trace(c, (body - c) / (float)pd, pd - .3).Hit) dmg = Math.Round(dmg * .5);
                if (dmg > 0)
                {
                    Hurt = .6;
                    Say?.Invoke("CAUGHT IN THE BLAST  -" + dmg);
                    Damage((int)dmg, cause);
                    if (!Active) return;
                }
            }
            Guards.Blast(c, guardR);
            Guards.MakeNoise(c.X, c.Z, GuardDirector.NoiseBlast, "blast");
            if (Math.Abs(carve - 3.7) < 1e-9)
            {
                int cracked = before - Destruction.CountMaterial(10, c, carve);
                Say?.Invoke(cracked > 0 && removed == 0 ? "REINFORCED WALL CRACKED · ONE MORE CHARGE" : removed + " CONCRETE CELLS CLEARED");
            }
        }

        void ArmDrums(Vector3 c, double r)
        {
            for (int z = (int)Math.Floor(c.Z - r); z <= c.Z + r; z++)
                for (int x = (int)Math.Floor(c.X - r); x <= c.X + r; x++)
                {
                    if (World.Tile(x, 1, z) != 15) continue;
                    double ddx = x + .5 - c.X, ddz = z + .5 - c.Z;
                    if (ddx * ddx + ddz * ddz > r * r) continue;
                    DetonateDrum(x, z, .12 + rng.NextDouble() * .18);
                }
        }

        void DetonateDrum(int x, int z, double delay)
        {
            foreach (var b in pendingBlasts) if (b.X == x && b.Z == z) return;
            pendingBlasts.Add((x, z, delay));
        }

        void StepBlasts(double dt)
        {
            if (pendingBlasts.Count == 0) return;
            var due = new List<(int X, int Z)>();
            for (int i = pendingBlasts.Count - 1; i >= 0; i--)
            {
                var b = pendingBlasts[i];
                b.T -= dt;
                if (b.T <= 0) { due.Add((b.X, b.Z)); pendingBlasts.RemoveAt(i); }
                else pendingBlasts[i] = b;
            }
            foreach (var (x, z) in due)
            {
                if (World.Tile(x, 1, z) != 15) continue;
                for (int y = 1; y < VoxelWorld.SizeY && World.Tile(x, y, z) == 15; y++) World.Set(x, y, z, 0);
                Destruction.Poke();
                var p = new Vector3(x + .5f, 2, z + .5f);
                Explode(p, 0, 0, p, 2.2, 7.5, 95, 5.5, "Too close to a fuel drum when it went up.");
                if (!Active) return;
            }
        }

        // ---------- health ----------

        public void Damage(int amount, string cause)
        {
            if (!Active) return;
            Hp = Math.Max(0, Hp - amount);
            Hurt = Math.Max(Hurt, .26);
            Sound?.Play("player_hit", .6f);
            if (Hp == 0) { Active = false; Died?.Invoke(cause); }
        }

        public void Step(double dt)
        {
            Cooldown = Math.Max(0, Cooldown - dt);
            Flash = Math.Max(0, Flash - dt);
            Hurt = Math.Max(0, Hurt - dt);
            if (Reload > 0)
            {
                Reload = Math.Max(0, Reload - dt);
                if (Reload == 0)
                {
                    int rounds = Math.Min(MagazineSize - Magazine, Reserve);
                    Magazine += rounds; Reserve -= rounds;
                }
            }
            if (Active && Input.IsActionPressed("fire") && Input.MouseMode == Input.MouseModeEnum.Captured) Fire();
            StepCharge(dt);
            if (!Active) return;
            StepGrenades(dt);
            if (!Active) return;
            StepBlasts(dt);
            if (Gun != null)
            {
                Gun.Cooldown = (float)Cooldown;
                Gun.Reload = (float)Reload;
                Gun.Flash = (float)Flash;
                Gun.PlantLeft = charge != null ? (float)charge.Plant : 0;
            }
        }
    }
}

using System;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// First-person movement and mouse look, ported from update() in bunker-buster.js:
    /// walk 6 voxels/s, crouch-walk 3, eye at 4.15 (2.65 crouched), collision against the voxel
    /// grid with the same canStand test, sliding along walls one axis at a time.
    /// Space jumps (about 2 voxels high: enough to hop sandbag lines, rubble and crates), and you can
    /// stand on top of things and drop off them.
    /// </summary>
    public partial class PlayerController : Node3D
    {
        public const double EyeStand = 4.15, EyeCrouch = 2.65, BodyStand = 4.4, BodyCrouch = 2.9;
        public const float MouseSensitivity = .0027f, PitchLimit = 1.12f;
        public const double HeightStand = BodyStand - 1, HeightCrouch = BodyCrouch - 1;
        public const double Gravity = 26, JumpSpeed = 10.6;

        public VoxelWorld World;
        public Camera3D Camera;
        public double X, Z, Eye = EyeStand;
        /// <summary>Height of the feet (1 = the ground floor), vertical speed, and whether standing on something.</summary>
        public double Feet = 1, Vy;
        public bool OnGround = true;
        /// <summary>Set for one frame when the player lands from a fall or jump (fall speed).</summary>
        public double Landed;
        public float Yaw, Pitch;
        public bool Crouched;
        /// <summary>Distance walked this frame (guards hear footsteps from it later).</summary>
        public double Travel;
        double stridePhase;
        public double StridePhase => stridePhase;

        public override void _Ready()
        {
            Camera = new Camera3D { Name = "Camera", Fov = 75, Near = .05f, Far = 200 };
            AddChild(Camera);
            Camera.MakeCurrent();
        }

        public void Spawn(double x, double z)
        {
            X = x; Z = z; Feet = 1; Vy = 0; OnGround = true; Eye = EyeStand; Yaw = Mathf.Pi; Pitch = 0; Crouched = false;   // face into the base (+z), toward the foyer exits
            ApplyTransform();
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                Yaw -= motion.Relative.X * MouseSensitivity;
                Pitch = Mathf.Clamp(Pitch - motion.Relative.Y * MouseSensitivity, -PitchLimit, PitchLimit);
            }
        }

        public void Step(double dt)
        {
            bool wantCrouch = Input.IsActionPressed("crouch");
            Crouched = wantCrouch || !World.FitsAt(X, Z, Feet, HeightStand);
            double height = Crouched ? HeightCrouch : HeightStand;
            double eyeTarget = Feet - 1 + (Crouched ? EyeCrouch : EyeStand);
            Eye = Crouched || !OnGround ? eyeTarget : Eye + (eyeTarget - Eye) * Math.Min(1, dt * 18);

            float forward = Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back");
            float strafe = Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left");
            double speed = (Crouched ? 3 : 6) * dt / Math.Max(1, Math.Sqrt(forward * forward + strafe * strafe));
            // Camera looks down -Z when Yaw is 0; its right is +X.
            double sin = Math.Sin(Yaw), cos = Math.Cos(Yaw);
            double fx = -sin, fz = -cos, rx = cos, rz = -sin;
            double mx = (fx * forward + rx * strafe) * speed, mz = (fz * forward + rz * strafe) * speed;
            double oldX = X, oldZ = Z;
            if (World.FitsAt(X + mx, Z, Feet, height)) X += mx;
            if (World.FitsAt(X, Z + mz, Feet, height)) Z += mz;
            Travel = OnGround ? Math.Sqrt((X - oldX) * (X - oldX) + (Z - oldZ) * (Z - oldZ)) : 0;
            stridePhase += Travel * 2.8;

            // jumping, falling and landing
            Landed = 0;
            if (dt > 0)
            {
                if (OnGround && Input.IsActionJustPressed("jump") && !Crouched) { Vy = JumpSpeed; OnGround = false; }
                double ground = World.GroundUnder(X, Z, Feet + .01);
                if (OnGround && ground < Feet - .01) OnGround = false;   // walked off an edge, or the floor was blown away
                if (!OnGround)
                {
                    Vy -= Gravity * dt;
                    double next = Feet + Vy * dt;
                    if (Vy > 0 && !World.FitsAt(X, Z, next, height)) { Vy = 0; next = Feet; }   // head hit something
                    ground = World.GroundUnder(X, Z, Feet + .01);
                    if (Vy <= 0 && next <= ground)
                    {
                        Landed = -Vy;
                        Feet = ground; Vy = 0; OnGround = true;
                    }
                    else Feet = next;
                    Eye = Feet - 1 + (Crouched ? EyeCrouch : EyeStand);
                }
            }
            ApplyTransform();
        }

        /// <summary>Forward direction the camera is looking (JS direction()).</summary>
        public Vector3 LookDirection => -Camera.GlobalTransform.Basis.Z;

        void ApplyTransform()
        {
            float bob = (float)(Math.Sin(stridePhase) * .05 * (Crouched ? .5 : 1));
            Position = new Vector3((float)X, (float)Eye + bob, (float)Z);
            Rotation = new Vector3(0, Yaw, 0);
            if (Camera != null) Camera.Rotation = new Vector3(Pitch, 0, 0);
        }
    }
}

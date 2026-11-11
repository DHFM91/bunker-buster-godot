using System;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// First-person movement and mouse look, ported from update() in bunker-buster.js:
    /// walk 6 voxels/s, crouch-walk 3, eye at 4.15 (2.65 crouched), collision against the voxel
    /// grid with the same canStand test, sliding along walls one axis at a time.
    /// </summary>
    public partial class PlayerController : Node3D
    {
        public const double EyeStand = 4.15, EyeCrouch = 2.65, BodyStand = 4.4, BodyCrouch = 2.9;
        public const float MouseSensitivity = .0027f, PitchLimit = 1.12f;

        public VoxelWorld World;
        public Camera3D Camera;
        public double X, Z, Eye = EyeStand;
        public float Yaw, Pitch;
        public bool Crouched;
        /// <summary>Distance walked this frame (guards hear footsteps from it later).</summary>
        public double Travel;
        double stridePhase;

        public override void _Ready()
        {
            Camera = new Camera3D { Name = "Camera", Fov = 75, Near = .05f, Far = 200 };
            AddChild(Camera);
            Camera.MakeCurrent();
        }

        public void Spawn(double x, double z)
        {
            X = x; Z = z; Eye = EyeStand; Yaw = 0; Pitch = 0; Crouched = false;
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
            Crouched = wantCrouch || !World.CanStand(X, Z, BodyStand);
            double eyeTarget = Crouched ? EyeCrouch : EyeStand;
            Eye = Crouched ? eyeTarget : Eye + (eyeTarget - Eye) * Math.Min(1, dt * 18);

            float forward = Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back");
            float strafe = Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left");
            double speed = (Crouched ? 3 : 6) * dt / Math.Max(1, Math.Sqrt(forward * forward + strafe * strafe));
            // Camera looks down -Z when Yaw is 0; its right is +X.
            double sin = Math.Sin(Yaw), cos = Math.Cos(Yaw);
            double fx = -sin, fz = -cos, rx = cos, rz = -sin;
            double mx = (fx * forward + rx * strafe) * speed, mz = (fz * forward + rz * strafe) * speed;
            double oldX = X, oldZ = Z, top = Crouched ? BodyCrouch : BodyStand;
            if (World.CanStand(X + mx, Z, top)) X += mx;
            if (World.CanStand(X, Z + mz, top)) Z += mz;
            Travel = Math.Sqrt((X - oldX) * (X - oldX) + (Z - oldZ) * (Z - oldZ));
            stridePhase += Travel * 2.8;
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

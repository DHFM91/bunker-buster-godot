using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// One soldier (JS makeGuard). Yaw uses the browser game's convention: facing (sin yaw, cos yaw)
    /// in x/z, so all the ported AI maths stays identical.
    /// </summary>
    public sealed class Guard
    {
        public double X, Z, Yaw;
        public int Cell, Hp = 2;
        public double Gait, Pace, Impact, Fall, Shot = 1.5, Aim, Spot, Muzzle, Query, Wait, Look, Scan;
        public bool Saw;
        public string Task = "idle";
        public Vector2? Goal;
        public short[] Field, PostField;
        public bool HasPost;
        public double PostX, PostZ, PostYaw;
        public int Pose;                 // which of the three downed poses
        public Sprite3D Sprite, Icon;

        public bool Alive => Hp > 0;
    }

    /// <summary>A guard's round in flight: visible and dodgeable.</summary>
    public sealed class Bullet
    {
        public Vector3 P, V;
        public double Life = 3;
        public MeshInstance3D Mesh;
    }
}

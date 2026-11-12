using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// Everything the soldier model needs to know about the guard this frame. The caller fills it in
    /// from the guard's current state and passes it to <see cref="GuardModel.Animate"/>.
    /// </summary>
    public struct GuardAnim
    {
        /// <summary>Walking speed as a fraction, 0 (standing still) to 1 (full pace).</summary>
        public float Pace;
        /// <summary>Walk cycle phase accumulator. The model uses sin(Gait * 1.4).</summary>
        public float Gait;
        /// <summary>Greater than zero while the guard raises the rifle to fire (counts down from 0.6).</summary>
        public float Aim;
        /// <summary>Greater than zero for 0.12 seconds right after a shot.</summary>
        public float Muzzle;
        /// <summary>Greater than zero for about 0.2 seconds after being hit.</summary>
        public float Impact;
        /// <summary>Death fall progress from 0 to 1. Only used when Alive is false.</summary>
        public float Fall;
        /// <summary>False once the guard is dead.</summary>
        public bool Alive;
        /// <summary>True when the base is on Caution, Evasion or Alert: the rifle is held at the ready instead of slung low.</summary>
        public bool Alert;
        /// <summary>Aim pitch in radians. Positive means the target is above.</summary>
        public float AimPitch;
        /// <summary>Extra head turn in radians, used for scanning.</summary>
        public float HeadYaw;
    }

    /// <summary>
    /// A procedural, articulated, low-poly 3D soldier built from primitive meshes and animated in code.
    /// The node origin is the soldier's feet on the floor and he faces local +Z. One unit is one voxel.
    /// The soldier's right side is local -X and his left side is local +X.
    /// Call <see cref="Build"/> once, then <see cref="Animate"/> every frame. The root node is never moved by this class.
    /// </summary>
    public partial class GuardModel : Node3D
    {
        // ------------------------------------------------------------------
        // Skeleton dimensions (units). Mirrored by the offline geometry check.
        // ------------------------------------------------------------------
        const float HipsY = 2.12f;       // hips joint height above the feet when standing
        const float HipX = 0.2f;         // hip joint sideways offset from the hips
        const float HipJointY = -0.05f;  // hip joint height relative to the hips
        const float ThighLen = 0.9f;
        const float ShinLen = 0.85f;
        const float AnkleH = 0.32f;      // ankle height above the floor
        const float SpineY = 0.15f;
        const float ChestY = 0.45f;
        const float NeckY = 0.82f;
        const float HeadY = 0.15f;
        const float ShX = 0.48f;
        const float ShY = 0.62f;
        const float UpperLen = 0.64f;
        const float ForeLen = 0.6f;

        // Joint indices used by the death pose tables.
        const int JHips = 0, JSpine = 1, JChest = 2, JNeck = 3, JHead = 4,
            JUpperL = 5, JForeL = 6, JHandL = 7, JUpperR = 8, JForeR = 9, JHandR = 10,
            JThighL = 11, JShinL = 12, JFootL = 13, JThighR = 14, JShinR = 15, JFootR = 16;
        const int JointCount = 17;
        // Table layout: 17 joint euler triples, hips position, rifle position, rifle euler, pivot euler.
        const int TabHips = 51, TabRiflePos = 54, TabRifleEul = 57, TabPivot = 60;

        // ------------------------------------------------------------------
        // Death pose tables. Euler angles use the order X, then Y, then Z (R = Rx * Ry * Rz).
        // The rifle position and angles are in the body's own space (so it ends up lying beside him).
        // ------------------------------------------------------------------
        // Pose 0: falls straight backward, flat on his back.
        static readonly float[] DeathA0 =
        {
            0f, 0f, 0f,        // hips
            0f, 0f, 0f,        // spine
            0f, 0f, 0f,        // chest
            0.1f, 0f, 0f,      // neck
            0.2f, 0f, 0.3f,    // head
            0.5f, 0f, 0.8f,    // upper arm L
            0.3f, 0f, 0f,      // forearm L
            0f, 0f, 0f,        // hand L
            0.4f, 0f, -0.9f,   // upper arm R
            0.6f, 0f, 0f,      // forearm R
            0f, 0f, 0f,        // hand R
            -0.1f, 0f, 0.3f,   // thigh L
            0.25f, 0f, 0f,     // shin L
            -0.1f, 0f, 0.5f,   // foot L
            0.2f, 0f, -0.25f,  // thigh R
            0.5f, 0f, 0f,      // shin R
            -0.2f, 0f, -0.4f,  // foot R
            0f, 2.12f, 0f,     // hips position
            0.95f, 2.2f, -0.42f, // rifle position
            -1.5708f, 0.2f, 0f,  // rifle euler
            -1.5708f, 0f, 0f     // pivot euler
        };

        // Pose 1, stage A: collapses onto his knees.
        static readonly float[] DeathA1 =
        {
            0f, 0f, 0f,
            0.2f, 0f, 0f,
            0.25f, 0f, 0f,
            0.1f, 0f, 0f,
            0.2f, 0f, 0f,
            0.3f, 0f, 0.15f,
            0.6f, 0f, 0f,
            0f, 0f, 0f,
            0.3f, 0f, -0.15f,
            0.6f, 0f, 0f,
            0f, 0f, 0f,
            -0.15f, 0f, 0.1f,
            1.7f, 0f, 0f,
            1.05f, 0f, 0f,
            -0.15f, 0f, -0.1f,
            1.7f, 0f, 0f,
            1.05f, 0f, 0f,
            0f, 2.12f, 0.1f,
            0.45f, 0.9f, 0.5f,
            1f, 0f, 0.3f,
            0.12f, 0f, 0f
        };

        // Pose 1, stage B: pitches forward and ends face down, one arm flung past the head.
        static readonly float[] DeathB1 =
        {
            0f, 0f, 0f,
            0.1f, 0.2f, 0f,
            0.05f, 0f, 0f,
            -0.2f, 0.6f, 0f,
            0f, 0.9f, 0f,
            2.7f, 0f, 0.4f,
            0.3f, 0f, 0f,
            0f, 0f, 0f,
            0.2f, 0f, -0.5f,
            0.5f, 0f, 0f,
            0f, 0f, 0f,
            0.15f, 0f, 0.15f,
            0.3f, 0f, 0f,
            1.2f, 0f, 0.2f,
            0.1f, 0f, -0.25f,
            0.8f, 0f, 0f,
            0.9f, 0f, 0f,
            0f, 2.12f, 0f,
            0.95f, 2.3f, 0.4f,
            -1.5708f, 0.3f, 0f,
            1.5708f, 0f, 0f
        };

        // Pose 2: twists and drops onto his left side.
        static readonly float[] DeathA2 =
        {
            0f, -0.4f, 0.1f,
            0.1f, 0.6f, 0f,
            0.1f, 0.5f, 0.1f,
            0f, 0.3f, 0.2f,
            0.1f, 0.4f, 0.3f,
            0.6f, 0f, 0.2f,
            1.2f, 0f, 0f,
            0f, 0f, 0f,
            -1.3f, 0f, -0.4f,
            0.4f, 0f, 0f,
            0f, 0f, 0f,
            -0.2f, 0f, 0.2f,
            0.5f, 0f, 0f,
            0f, 0f, 0.3f,
            -1.1f, 0f, -0.3f,
            1.5f, 0f, 0f,
            0f, 0f, -0.2f,
            0f, 2.12f, 0f,
            0.45f, 2.3f, 0.95f,
            -1.5708f, 0.4f, 0f,
            0f, 0.5f, -1.5708f
        };

        // Sideways nudge (x, z pairs per pose) so each finished corpse is centred within about 2.3 units of the origin.
        static readonly float[] DeathShift = { 0f, 0.14f, 0f, -0.17f, -0.2f, 0f };

        static readonly float[][] DeathA = { DeathA0, DeathA1, DeathA2 };
        static readonly float[][] DeathB = { DeathA0, DeathB1, DeathA2 };

        // ------------------------------------------------------------------
        // Shared (static) resources
        // ------------------------------------------------------------------
        static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, StandardMaterial3D> MatCache = new Dictionary<string, StandardMaterial3D>();
        static readonly StandardMaterial3D[] SkinMats = new StandardMaterial3D[4];
        static readonly PaletteSet[] Sets = new PaletteSet[4];
        static bool _staticsReady;
        static StandardMaterial3D _gun, _gunHi, _strap, _mask, _lens, _buckle, _pad, _webbing, _belt, _sole, _glove, _flashMat, _optic;

        sealed class PaletteSet
        {
            public StandardMaterial3D Camo, Vest, Pouch, PouchDk, Helmet, Boot, BootHi;
        }

        static readonly Color[][] CamoCols =
        {
            // woodland olive
            new Color[] { C(104, 106, 78), C(80, 86, 60), C(116, 100, 74), C(52, 56, 42) },
            // desert tan
            new Color[] { C(176, 150, 108), C(150, 124, 86), C(196, 172, 128), C(110, 90, 62) },
            // urban grey
            new Color[] { C(120, 124, 128), C(92, 96, 102), C(150, 152, 154), C(58, 60, 66) },
            // dark night
            new Color[] { C(44, 50, 46), C(32, 38, 36), C(58, 60, 52), C(18, 20, 22) }
        };

        // vest, pouch, pouch dark, helmet colour, boot, boot highlight
        static readonly Color[][] Gear =
        {
            new Color[] { C(64, 72, 54), C(78, 86, 64), C(44, 50, 38), C(84, 94, 68), C(78, 64, 46), C(104, 86, 62) },
            new Color[] { C(138, 118, 86), C(156, 134, 98), C(98, 82, 58), C(160, 138, 100), C(96, 78, 54), C(120, 100, 72) },
            new Color[] { C(70, 74, 80), C(88, 92, 98), C(46, 48, 54), C(96, 100, 106), C(52, 50, 48), C(74, 72, 70) },
            new Color[] { C(30, 34, 32), C(40, 44, 42), C(22, 24, 24), C(36, 40, 38), C(34, 30, 26), C(48, 44, 38) }
        };

        static readonly Color[] SkinCols = { C(198, 150, 110), C(172, 122, 86), C(134, 92, 62), C(96, 66, 46) };

        static Color C(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        // ------------------------------------------------------------------
        // Instance state
        // ------------------------------------------------------------------
        bool _built;
        int _pose;
        Node3D _pivot, _hips, _spine, _chest, _neck, _head, _ribs;
        Node3D _shL, _upL, _foL, _haL, _shR, _upR, _foR, _haR;
        Node3D _thL, _snL, _ftL, _thR, _snR, _ftR;
        Node3D _rifle, _flash;
        OmniLight3D _flashLight;
        readonly Node3D[] _joints = new Node3D[JointCount];

        float _t, _pace, _aimW, _alertW, _impactW, _recoil, _headYawS, _aimPitchS;
        bool _wasDead, _snapTaken, _settled;
        readonly Quaternion[] _snapRot = new Quaternion[JointCount];
        Vector3 _snapHips;
        Transform3D _snapRifle;

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the soldier's skeleton and meshes. The look value (0 up to but not including 1)
        /// deterministically picks one of four skin tones and one of four camouflage palettes.
        /// The death pose (0, 1 or 2) picks how he falls: backward, forward onto his knees, or twisting to his side.
        /// </summary>
        public void Build(float look, int deathPose)
        {
            foreach (Node child in GetChildren())
            {
                RemoveChild(child);
                child.QueueFree();
            }
            _built = false;
            EnsureStatics();

            float lk = look - Mathf.Floor(look);
            float s4 = lk * 4f;
            int skinIdx = Mathf.Clamp((int)Mathf.Floor(s4), 0, 3);
            int pal = Mathf.Clamp((int)((s4 - Mathf.Floor(s4)) * 4f), 0, 3);
            _pose = Mathf.Clamp(deathPose, 0, 2);

            PaletteSet ps = GetSet(pal);
            StandardMaterial3D skin = SkinMats[skinIdx];

            // ---- skeleton ----
            _pivot = Joint(this, "Pivot", 0f, 0f, 0f);
            _hips = Joint(_pivot, "Hips", 0f, HipsY, 0f);
            _spine = Joint(_hips, "Spine", 0f, SpineY, 0f);
            _chest = Joint(_spine, "Chest", 0f, ChestY, 0f);
            _ribs = Joint(_chest, "Ribs", 0f, 0f, 0f);
            _neck = Joint(_chest, "Neck", 0f, NeckY, 0f);
            _head = Joint(_neck, "Head", 0f, HeadY, 0f);

            _shL = Joint(_chest, "ShoulderL", ShX, ShY, 0f);
            _upL = Joint(_shL, "UpperArmL", 0f, 0f, 0f);
            _foL = Joint(_upL, "ForearmL", 0f, -UpperLen, 0f);
            _haL = Joint(_foL, "HandL", 0f, -ForeLen, 0f);
            _shR = Joint(_chest, "ShoulderR", -ShX, ShY, 0f);
            _upR = Joint(_shR, "UpperArmR", 0f, 0f, 0f);
            _foR = Joint(_upR, "ForearmR", 0f, -UpperLen, 0f);
            _haR = Joint(_foR, "HandR", 0f, -ForeLen, 0f);

            _thL = Joint(_hips, "ThighL", HipX, HipJointY, 0f);
            _snL = Joint(_thL, "ShinL", 0f, -ThighLen, 0f);
            _ftL = Joint(_snL, "FootL", 0f, -ShinLen, 0f);
            _thR = Joint(_hips, "ThighR", -HipX, HipJointY, 0f);
            _snR = Joint(_thR, "ShinR", 0f, -ThighLen, 0f);
            _ftR = Joint(_snR, "FootR", 0f, -ShinLen, 0f);

            _rifle = Joint(_chest, "Rifle", -0.25f, 0.25f, 0.4f);

            _joints[JHips] = _hips; _joints[JSpine] = _spine; _joints[JChest] = _chest;
            _joints[JNeck] = _neck; _joints[JHead] = _head;
            _joints[JUpperL] = _upL; _joints[JForeL] = _foL; _joints[JHandL] = _haL;
            _joints[JUpperR] = _upR; _joints[JForeR] = _foR; _joints[JHandR] = _haR;
            _joints[JThighL] = _thL; _joints[JShinL] = _snL; _joints[JFootL] = _ftL;
            _joints[JThighR] = _thR; _joints[JShinR] = _snR; _joints[JFootR] = _ftR;

            // ---- head ----
            Node3D head = _head;
            Box(head, skin, 0.46f, 0.46f, 0.46f, 0f, 0.12f, 0.1f);                       // face
            Box(head, _mask, 0.5f, 0.22f, 0.5f, 0f, -0.01f, 0.1f);                       // balaclava lower half
            Shadow(Sph(head, ps.Camo, 0.5f, 0.78f, 0.52f, 0.85f, 0f, 0.27f, -0.06f));   // helmet shell with cloth cover
            Box(head, _strap, 0.8f, 0.06f, 0.87f, 0f, 0.28f, -0.06f);                    // helmet cover band (at the shell's widest point)
            BoxR(head, ps.Helmet, 0.7f, 0.04f, 0.14f, 0f, 0.3f, 0.38f, 8f, 0f, 0f);      // brim
            Box(head, _gun, 0.14f, 0.1f, 0.1f, 0f, 0.36f, 0.43f);                        // NVG mount
            Box(head, _strap, 0.8f, 0.08f, 0.84f, 0f, 0.17f, -0.06f);                    // goggles strap
            Box(head, _lens, 0.17f, 0.12f, 0.08f, 0.12f, 0.17f, 0.38f);                  // lens left
            Box(head, _lens, 0.17f, 0.12f, 0.08f, -0.12f, 0.17f, 0.38f);                 // lens right
            Box(head, _strap, 0.03f, 0.4f, 0.05f, 0.245f, 0.1f, 0.12f);                  // chin strap left
            Box(head, _strap, 0.03f, 0.4f, 0.05f, -0.245f, 0.1f, 0.12f);                 // chin strap right
            Cyl(_neck, skin, 0.14f, 0.16f, 0.32f, 0f, 0.08f, 0f, 0f, 0f, 0f);            // neck

            // ---- torso ----
            Node3D ribs = _ribs;
            Shadow(Box(ribs, ps.Camo, 0.78f, 0.85f, 0.48f, 0f, 0.375f, 0f));             // shirt
            Box(ribs, ps.Vest, 0.56f, 0.6f, 0.1f, 0f, 0.38f, 0.29f);                     // front plate
            Box(ribs, ps.Vest, 0.56f, 0.6f, 0.1f, 0f, 0.38f, -0.29f);                    // back plate
            Box(ribs, ps.Pouch, 0.15f, 0.2f, 0.1f, -0.18f, 0.18f, 0.37f);                // magazine pouch 1
            Box(ribs, ps.Pouch, 0.15f, 0.2f, 0.1f, 0f, 0.18f, 0.37f);                    // magazine pouch 2
            Box(ribs, ps.Pouch, 0.15f, 0.2f, 0.1f, 0.18f, 0.18f, 0.37f);                 // magazine pouch 3
            Box(ribs, _webbing, 0.5f, 0.03f, 0.03f, 0f, 0.5f, 0.35f);                    // webbing row
            Box(ribs, ps.PouchDk, 0.1f, 0.3f, 0.2f, 0.44f, 0.3f, -0.05f);                // radio pouch (left side)
            Cyl(ribs, _gun, 0.015f, 0.015f, 0.5f, 0.45f, 0.7f, -0.1f, -8f, 0f, 0f);      // radio antenna
            Shadow(Box(ribs, ps.PouchDk, 0.5f, 0.62f, 0.16f, 0f, 0.35f, -0.43f));        // hydration pack
            Box(ribs, ps.Vest, 0.14f, 0.06f, 0.5f, 0.2f, 0.83f, 0f);                     // shoulder strap left
            Box(ribs, ps.Vest, 0.14f, 0.06f, 0.5f, -0.2f, 0.83f, 0f);                    // shoulder strap right
            Shadow(Box(_spine, ps.Camo, 0.74f, 0.5f, 0.44f, 0f, 0.17f, 0f));             // abdomen
            Shadow(Box(_hips, ps.Camo, 0.74f, 0.34f, 0.46f, 0f, 0f, 0f));                // pelvis (trousers)
            Box(_hips, _belt, 0.8f, 0.12f, 0.5f, 0f, 0.12f, 0f);                         // belt
            Box(_hips, _buckle, 0.14f, 0.1f, 0.04f, 0f, 0.12f, 0.26f);                   // buckle

            // ---- legs (left is +X, right is -X) ----
            Shadow(Box(_thL, ps.Camo, 0.34f, 0.92f, 0.38f, 0f, -0.45f, 0f));             // thigh L
            Box(_thL, ps.PouchDk, 0.1f, 0.3f, 0.26f, 0.22f, -0.3f, 0.02f);               // cargo pocket
            Shadow(Box(_snL, ps.Camo, 0.3f, 0.86f, 0.34f, 0f, -0.43f, 0f));              // shin L
            Box(_snL, _pad, 0.26f, 0.24f, 0.1f, 0f, -0.06f, 0.2f);                       // knee pad L
            Box(_ftL, ps.Boot, 0.32f, 0.28f, 0.6f, 0f, -0.12f, 0.1f);                    // boot L
            Box(_ftL, _sole, 0.34f, 0.07f, 0.64f, 0f, -0.285f, 0.1f);                    // sole L
            Shadow(Box(_thR, ps.Camo, 0.34f, 0.92f, 0.38f, 0f, -0.45f, 0f));             // thigh R
            Box(_thR, _gun, 0.1f, 0.34f, 0.22f, -0.22f, -0.35f, 0.02f);                  // holster
            Shadow(Box(_snR, ps.Camo, 0.3f, 0.86f, 0.34f, 0f, -0.43f, 0f));              // shin R
            Box(_snR, _pad, 0.26f, 0.24f, 0.1f, 0f, -0.06f, 0.2f);                       // knee pad R
            Box(_ftR, ps.Boot, 0.32f, 0.28f, 0.6f, 0f, -0.12f, 0.1f);                    // boot R
            Box(_ftR, _sole, 0.34f, 0.07f, 0.64f, 0f, -0.285f, 0.1f);                    // sole R

            // ---- arms ----
            Box(_upL, ps.Camo, 0.22f, 0.62f, 0.22f, 0f, -0.31f, 0f);                     // sleeve upper L
            Box(_foL, ps.Camo, 0.19f, 0.58f, 0.19f, 0f, -0.29f, 0f);                     // sleeve lower L
            Box(_haL, _glove, 0.17f, 0.15f, 0.22f, 0f, 0f, 0f);                          // glove L
            Box(_upR, ps.Camo, 0.22f, 0.62f, 0.22f, 0f, -0.31f, 0f);                     // sleeve upper R
            Box(_foR, ps.Camo, 0.19f, 0.58f, 0.19f, 0f, -0.29f, 0f);                     // sleeve lower R
            Box(_haR, _glove, 0.17f, 0.15f, 0.22f, 0f, 0f, 0f);                          // glove R

            // ---- rifle ----
            Node3D rf = _rifle;
            Box(rf, _gun, 0.14f, 0.2f, 0.42f, 0f, 0f, 0f);                               // receiver
            Box(rf, _gun, 0.11f, 0.19f, 0.26f, 0f, -0.03f, -0.33f);                      // stock
            Box(rf, _gunHi, 0.12f, 0.22f, 0.04f, 0f, -0.03f, -0.47f);                    // butt pad
            BoxR(rf, _gun, 0.08f, 0.22f, 0.09f, 0f, -0.19f, -0.1f, 15f, 0f, 0f);         // pistol grip
            Box(rf, _gunHi, 0.12f, 0.15f, 0.36f, 0f, -0.01f, 0.38f);                     // handguard
            Box(rf, _gun, 0.06f, 0.03f, 0.6f, 0f, 0.115f, 0.15f);                        // top rail
            Box(rf, _optic, 0.1f, 0.11f, 0.24f, 0f, 0.18f, 0f);                          // optic
            Cyl(rf, _gun, 0.035f, 0.035f, 0.3f, 0f, 0f, 0.7f, 90f, 0f, 0f);              // barrel
            Cyl(rf, _gunHi, 0.05f, 0.05f, 0.1f, 0f, 0f, 0.88f, 90f, 0f, 0f);             // muzzle brake
            BoxR(rf, _gun, 0.1f, 0.32f, 0.15f, 0f, -0.25f, 0.08f, -10f, 0f, 0f);         // magazine
            Box(rf, _strap, 0.03f, 0.04f, 1.1f, 0.09f, -0.06f, 0.05f);                   // sling

            _flash = Joint(rf, "Flash", 0f, 0f, 0.98f);
            Box(_flash, _flashMat, 0.26f, 0.04f, 0.04f, 0f, 0f, 0f);
            Box(_flash, _flashMat, 0.04f, 0.26f, 0.04f, 0f, 0f, 0f);
            Box(_flash, _flashMat, 0.05f, 0.05f, 0.22f, 0f, 0f, 0.06f);
            _flashLight = new OmniLight3D();
            _flashLight.LightColor = new Color(1f, 0.72f, 0.38f, 1f);
            _flashLight.LightEnergy = 2.5f;
            _flashLight.OmniRange = 5f;
            _flashLight.ShadowEnabled = false;
            _flashLight.Position = new Vector3(0f, 0f, 0.1f);
            _flash.AddChild(_flashLight);
            _flash.Visible = false;

            // reset animation state
            _t = 0f; _pace = 0f; _aimW = 0f; _alertW = 0f; _impactW = 0f; _recoil = 0f;
            _headYawS = 0f; _aimPitchS = 0f;
            _wasDead = false; _snapTaken = false; _settled = false;
            _built = true;

            // settle into a neutral standing pose straight away
            GuardAnim idle = new GuardAnim();
            idle.Alive = true;
            AnimateAlive(0f, idle);
        }

        // ------------------------------------------------------------------
        // Animate
        // ------------------------------------------------------------------

        /// <summary>
        /// Advances the animation by dt seconds and poses the skeleton from the given state.
        /// Does nothing if <see cref="Build"/> has not been called yet.
        /// </summary>
        public void Animate(double dt, GuardAnim a)
        {
            if (!_built) return;
            float d = (float)dt;
            if (d < 0f) d = 0f;
            if (d > 0.1f) d = 0.1f;
            _t += d;

            if (a.Alive)
            {
                if (_wasDead)
                {
                    _wasDead = false;
                    _snapTaken = false;
                    _settled = false;
                    _pivot.Basis = Basis.Identity;
                    _pivot.Position = Vector3.Zero;
                }
                AnimateAlive(d, a);
            }
            else
            {
                _wasDead = true;
                AnimateDead(a);
            }
        }

        static Basis Rot(float x, float y, float z)
        {
            return new Basis(new Vector3(1f, 0f, 0f), x) * new Basis(new Vector3(0f, 1f, 0f), y) * new Basis(new Vector3(0f, 0f, 1f), z);
        }

        static Basis LookDown(Vector3 dir, Vector3 hint)
        {
            Vector3 y = -dir.Normalized();
            Vector3 z = hint - y * hint.Dot(y);
            if (z.LengthSquared() < 1e-6f) z = new Vector3(0f, 0f, 1f);
            z = z.Normalized();
            Vector3 x = y.Cross(z);
            return new Basis(x, y, z);
        }

        // Two-bone arm solve in chest space. The hand ends exactly on the target and takes the given basis.
        static void SolveArm(Node3D upper, Node3D fore, Node3D hand, Vector3 shoulder, Vector3 target, Basis handBasis, float side)
        {
            Vector3 d = target - shoulder;
            float maxReach = UpperLen + ForeLen - 0.02f;
            float dist = d.Length();
            if (dist < 0.05f)
            {
                d = new Vector3(0f, -0.05f, 0f);
                dist = 0.05f;
            }
            if (dist > maxReach)
            {
                d = d * (maxReach / dist);
                dist = maxReach;
                target = shoulder + d;
            }
            Vector3 dir = d / dist;
            float a = (UpperLen * UpperLen - ForeLen * ForeLen + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, UpperLen * UpperLen - a * a));
            Vector3 pole = new Vector3(side * 0.25f, -1f, -0.25f);
            Vector3 pp = pole - dir * pole.Dot(dir);
            if (pp.LengthSquared() < 1e-4f) pp = new Vector3(side, 0f, 0f);
            pp = pp.Normalized();
            Vector3 elbow = shoulder + dir * a + pp * h;
            Basis bu = LookDown(elbow - shoulder, pp);
            Basis bf = LookDown(target - elbow, pp);
            upper.Basis = bu;
            fore.Basis = bu.Inverse() * bf;
            hand.Basis = bf.Inverse() * handBasis;
        }

        // How far a foot must tip toe-down so that its toe reaches the floor when the ankle is delta higher than normal.
        static float ToePitch(float delta)
        {
            if (delta <= 0f) return 0f;
            float al = delta / 0.42f;
            al = (delta + 0.16f * al * al) / 0.42f;
            return Mathf.Clamp(al, 0f, 0.7f);
        }

        static float Rate(float rate, float d)
        {
            return 1f - Mathf.Exp(-rate * d);
        }

        void AnimateAlive(float d, GuardAnim a)
        {
            // ---- smoothed state ----
            _pace = Mathf.Lerp(_pace, Mathf.Clamp(a.Pace, 0f, 1f), Rate(8f, d));
            float aimTarget = a.Aim > 0f ? 1f : 0f;
            _aimW = Mathf.Lerp(_aimW, aimTarget, Rate(aimTarget > _aimW ? 18f : 6f, d));
            float alertTarget = (a.Alert || a.Aim > 0f) ? 1f : 0f;
            _alertW = Mathf.Lerp(_alertW, alertTarget, Rate(5f, d));
            float impTarget = Mathf.Clamp(a.Impact / 0.2f, 0f, 1f);
            _impactW = Mathf.Lerp(_impactW, impTarget, Rate(30f, d));
            float recTarget = Mathf.Clamp(a.Muzzle / 0.12f, 0f, 1f);
            _recoil = Mathf.Lerp(_recoil, recTarget, Rate(recTarget > _recoil ? 70f : 14f, d));
            _headYawS = Mathf.Lerp(_headYawS, a.HeadYaw, Rate(8f, d));
            _aimPitchS = Mathf.Lerp(_aimPitchS, Mathf.Clamp(a.AimPitch, -0.7f, 0.7f), Rate(12f, d));

            float aimW = _aimW, alertW = _alertW, imp = _impactW, rec = _recoil;
            float walk = _pace * (1f - aimW);
            float p = a.Gait * 1.4f;
            float sp = Mathf.Sin(p), cp = Mathf.Cos(p);
            float breathe = Mathf.Sin(_t * 1.7f);
            float shift = Mathf.Sin(_t * 0.65f) * (1f - walk);

            // ---- legs ----
            float amp = 0.5f * walk;
            float thetaL = -amp * sp - 0.25f * aimW;
            float thetaR = amp * sp + 0.25f * aimW;
            float kneeL = 0.04f + 0.9f * walk * Mathf.Max(0f, cp) + 0.3f * aimW;
            float kneeR = 0.04f + 0.9f * walk * Mathf.Max(0f, -cp) + 0.38f * aimW;
            float splay = 0.05f + 0.1f * aimW;

            Basis btL = Rot(thetaL, 0f, splay);
            Basis bsL = btL * Rot(kneeL, 0f, 0f);
            Basis btR = Rot(thetaR, 0f, -splay);
            Basis bsR = btR * Rot(kneeR, 0f, 0f);
            _thL.Basis = btL;
            _snL.Basis = Rot(kneeL, 0f, 0f);
            // (foot orientation is set below, once the ankle heights are known)
            _thR.Basis = btR;
            _snR.Basis = Rot(kneeR, 0f, 0f);

            Vector3 ankleL = btL * new Vector3(0f, -ThighLen, 0f) + bsL * new Vector3(0f, -ShinLen, 0f);
            Vector3 ankleR = btR * new Vector3(0f, -ThighLen, 0f) + bsR * new Vector3(0f, -ShinLen, 0f);
            float reach = Mathf.Max(-ankleL.Y, -ankleR.Y);
            // the foot on the shorter leg tips toe-down (heel raised) so its toe still meets the floor
            _ftL.Basis = bsL.Inverse() * Rot(ToePitch(reach + ankleL.Y), 0f, 0f);
            _ftR.Basis = bsR.Inverse() * Rot(ToePitch(reach + ankleR.Y), 0f, 0f);
            float hipsY = reach + AnkleH - HipJointY + 0.03f;

            // ---- hips, spine, chest ----
            float hipsRoll = 0.02f * shift + 0.04f * sp * walk;
            float hipsYaw = 0.12f * sp * walk - 0.35f * aimW;
            _hips.Position = new Vector3(shift * 0.03f, hipsY - 0.01f * breathe, -0.06f * imp);
            _hips.Basis = Rot(0f, hipsYaw, hipsRoll);

            float spinePitch = 0.03f + 0.1f * walk + 0.1f * aimW - 0.05f * rec - 0.12f * imp;
            float chestPitch = 0.02f * breathe + 0.06f * aimW - 0.06f * rec - 0.16f * imp;
            _spine.Basis = Rot(spinePitch, -0.12f * sp * walk + 0.35f * aimW, -hipsRoll * 0.8f);
            _chest.Basis = Rot(chestPitch, -0.08f * sp * walk, 0f);
            float lean = spinePitch + chestPitch;

            _ribs.Scale = new Vector3(1f + 0.012f * breathe, 1f + 0.01f * breathe, 1f + 0.02f * breathe);

            // ---- neck and head ----
            float hy = _headYawS;
            _neck.Basis = Rot(-0.1f * imp, 0.4f * hy, 0f);
            float headPitch = 0.02f * breathe + aimW * (0.1f - 0.8f * _aimPitchS - 0.8f * lean) - 0.25f * imp + 0.04f * rec;
            _head.Basis = Rot(headPitch, 0.6f * hy - 0.1f * aimW, 0f);

            // ---- rifle target ----
            Vector3 low = new Vector3(-0.25f, 0.25f, 0.4f);
            Vector3 ready = new Vector3(-0.25f, 0.38f, 0.45f);
            Vector3 aim = new Vector3(-0.3f, 0.78f, 0.4f);
            Vector3 rp = low.Lerp(ready, alertW).Lerp(aim, aimW);
            float lowPitch = 0.6f, readyPitch = 0.12f;
            float rPitch = Mathf.Lerp(Mathf.Lerp(lowPitch, readyPitch, alertW), -_aimPitchS - lean, aimW);
            float rYaw = 0.1f * (1f - aimW);

            rp.Y += 0.03f * Mathf.Sin(2f * p) * walk + 0.01f * breathe + 0.03f * rec;
            rp.X += 0.02f * sp * walk;
            rp.Z -= 0.14f * rec;
            rPitch += 0.04f * Mathf.Cos(2f * p) * walk - 0.1f * rec;
            Basis rb = Rot(rPitch, rYaw, 0f);
            _rifle.Position = rp;
            _rifle.Basis = rb;

            // ---- arms: both hands on the rifle ----
            float shrug = 0.04f * rec;
            _shL.Position = new Vector3(ShX, ShY + shrug, -0.03f * rec);
            _shR.Position = new Vector3(-ShX, ShY + shrug, -0.03f * rec);
            Vector3 gripR = rp + rb * new Vector3(0f, -0.16f, -0.1f);
            Vector3 gripL = rp + rb * new Vector3(0f, -0.12f, 0.32f);
            SolveArm(_upR, _foR, _haR, _shR.Position, gripR, rb, -1f);
            SolveArm(_upL, _foL, _haL, _shL.Position, gripL, rb, 1f);

            // ---- muzzle flash ----
            bool flashing = a.Muzzle > 0f;
            _flash.Visible = flashing;
            if (flashing)
            {
                float fs = 0.8f + 0.5f * Mathf.Abs(Mathf.Sin(_t * 91f));
                _flash.Scale = new Vector3(fs, fs, fs);
                _flash.RotationDegrees = new Vector3(0f, 0f, (_t * 3000f) % 360f);
            }
        }

        // ------------------------------------------------------------------
        // Death
        // ------------------------------------------------------------------

        static readonly Vector3[] ProbeHead = BoxCorners(0.4f, -0.1f, 0.55f, -0.45f, 0.5f);
        static readonly Vector3[] ProbeChest = BoxCorners(0.45f, -0.05f, 0.85f, -0.55f, 0.45f);
        static readonly Vector3[] ProbeHips = BoxCorners(0.4f, -0.17f, 0.2f, -0.26f, 0.26f);
        static readonly Vector3[] ProbeThighL = LegPoints(0.3f, 0.45f);
        static readonly Vector3[] ProbeThighR = LegPoints(-0.3f, -0.45f);
        static readonly Vector3[] ProbeShin =
        {
            new Vector3(0.17f, -0.86f, 0.18f), new Vector3(-0.17f, -0.86f, 0.18f),
            new Vector3(0.17f, -0.86f, -0.18f), new Vector3(-0.17f, -0.86f, -0.18f),
            new Vector3(0f, -0.06f, 0.27f), new Vector3(0f, -0.06f, -0.17f)
        };
        static readonly Vector3[] ProbeFoot =
        {
            new Vector3(0.17f, -0.32f, -0.22f), new Vector3(-0.17f, -0.32f, -0.22f),
            new Vector3(0.17f, -0.32f, 0.42f), new Vector3(-0.17f, -0.32f, 0.42f),
            new Vector3(0.17f, 0.02f, 0.4f), new Vector3(-0.17f, 0.02f, 0.4f),
            new Vector3(0.17f, 0.02f, -0.2f), new Vector3(-0.17f, 0.02f, -0.2f)
        };
        static readonly Vector3[] ProbeUpper =
        {
            new Vector3(0.11f, -0.62f, 0.11f), new Vector3(-0.11f, -0.62f, 0.11f),
            new Vector3(0.11f, -0.62f, -0.11f), new Vector3(-0.11f, -0.62f, -0.11f),
            new Vector3(0.12f, 0f, 0f), new Vector3(-0.12f, 0f, 0f)
        };
        static readonly Vector3[] ProbeFore =
        {
            new Vector3(0.1f, -0.58f, 0.1f), new Vector3(-0.1f, -0.58f, 0.1f),
            new Vector3(0.1f, -0.58f, -0.1f), new Vector3(-0.1f, -0.58f, -0.1f)
        };
        static readonly Vector3[] ProbeHand = BoxCorners(0.09f, -0.08f, 0.08f, -0.11f, 0.11f);
        static readonly Vector3[] ProbeRifle =
        {
            new Vector3(0f, 0.11f, -0.49f), new Vector3(0f, -0.14f, -0.49f),
            new Vector3(0f, 0f, 0.93f), new Vector3(0f, -0.41f, 0.08f), new Vector3(0f, 0.24f, 0f),
            new Vector3(0.06f, -0.085f, 0.56f), new Vector3(-0.06f, -0.085f, 0.56f),
            new Vector3(0.05f, -0.05f, 0.93f), new Vector3(-0.05f, -0.05f, 0.93f),
            new Vector3(0.05f, 0.05f, 0.93f), new Vector3(-0.05f, 0.05f, 0.93f)
        };

        static Vector3[] BoxCorners(float hx, float y0, float y1, float z0, float z1)
        {
            return new Vector3[]
            {
                new Vector3(hx, y0, z0), new Vector3(-hx, y0, z0), new Vector3(hx, y0, z1), new Vector3(-hx, y0, z1),
                new Vector3(hx, y1, z0), new Vector3(-hx, y1, z0), new Vector3(hx, y1, z1), new Vector3(-hx, y1, z1)
            };
        }

        static Vector3[] LegPoints(float xOuter, float xInner)
        {
            return new Vector3[]
            {
                new Vector3(xOuter, -0.9f, 0.2f), new Vector3(xOuter, -0.9f, -0.2f),
                new Vector3(xInner * 0.4f, -0.9f, 0.2f), new Vector3(xInner * 0.4f, -0.9f, -0.2f),
                new Vector3(xOuter, -0.3f, 0.2f), new Vector3(xOuter, -0.3f, -0.2f)
            };
        }

        static float MinY(Basis pb, Transform3D t, Vector3[] pts, float best)
        {
            for (int i = 0; i < pts.Length; i++)
            {
                float y = (pb * (t * pts[i])).Y;
                if (y < best) best = y;
            }
            return best;
        }

        static float Ease(float t)
        {
            t = Mathf.Clamp(t, 0f, 1f);
            return 1f - (1f - t) * (1f - t);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        static Basis TabRot(float[] tab, int index)
        {
            return Rot(tab[index], tab[index + 1], tab[index + 2]);
        }

        void TakeSnapshot()
        {
            for (int i = 0; i < JointCount; i++) _snapRot[i] = _joints[i].Quaternion;
            _snapHips = _hips.Position;
            Transform3D chestT = _hips.Transform * _spine.Transform * _chest.Transform;
            _snapRifle = chestT * _rifle.Transform;
            _snapTaken = true;
            _settled = false;
        }

        void AnimateDead(GuardAnim a)
        {
            _flash.Visible = false;
            if (!_snapTaken) TakeSnapshot();
            if (_settled) return;

            float f = Mathf.Clamp(a.Fall, 0f, 1f);
            float ea, eb;
            if (_pose == 1)
            {
                ea = Ease(f / 0.5f);
                eb = Smooth((f - 0.45f) / 0.55f);
            }
            else
            {
                ea = Ease(f);
                eb = 0f;
            }
            float[] ta = DeathA[_pose];
            float[] tb = DeathB[_pose];

            // joint rotations: snapshot -> stage A -> stage B
            for (int i = 0; i < JointCount; i++)
            {
                Quaternion qa = TabRot(ta, i * 3).GetRotationQuaternion();
                Quaternion qb = TabRot(tb, i * 3).GetRotationQuaternion();
                Quaternion q = _snapRot[i].Slerp(qa, ea);
                if (eb > 0f) q = q.Slerp(qb, eb);
                _joints[i].Quaternion = q;
            }

            Vector3 hipsA = new Vector3(ta[TabHips], ta[TabHips + 1], ta[TabHips + 2]);
            Vector3 hipsB = new Vector3(tb[TabHips], tb[TabHips + 1], tb[TabHips + 2]);
            Vector3 hp = _snapHips.Lerp(hipsA, ea);
            if (eb > 0f) hp = hp.Lerp(hipsB, eb);
            _hips.Position = hp;

            // keep the shoulders at rest
            _shL.Position = new Vector3(ShX, ShY, 0f);
            _shR.Position = new Vector3(-ShX, ShY, 0f);
            _ribs.Scale = Vector3.One;

            // pivot rotation
            Vector3 pivA = new Vector3(ta[TabPivot], ta[TabPivot + 1], ta[TabPivot + 2]);
            Vector3 pivB = new Vector3(tb[TabPivot], tb[TabPivot + 1], tb[TabPivot + 2]);
            Vector3 pe = pivA * ea;
            if (eb > 0f) pe = pe.Lerp(pivB, eb);
            Basis pb = Rot(pe.X, pe.Y, pe.Z);
            _pivot.Basis = pb;

            // the rifle drops loose: it leaves the hands and settles beside the body
            Transform3D chestT = _hips.Transform * _spine.Transform * _chest.Transform;
            Vector3 rpA = new Vector3(ta[TabRiflePos], ta[TabRiflePos + 1], ta[TabRiflePos + 2]);
            Vector3 rpB = new Vector3(tb[TabRiflePos], tb[TabRiflePos + 1], tb[TabRiflePos + 2]);
            Quaternion rqA = TabRot(ta, TabRifleEul).GetRotationQuaternion();
            Quaternion rqB = TabRot(tb, TabRifleEul).GetRotationQuaternion();
            Vector3 rpos = _snapRifle.Origin.Lerp(rpA, ea);
            Quaternion rq = _snapRifle.Basis.GetRotationQuaternion().Slerp(rqA, ea);
            if (eb > 0f)
            {
                rpos = rpos.Lerp(rpB, eb);
                rq = rq.Slerp(rqB, eb);
            }
            Transform3D rifleBody = new Transform3D(new Basis(rq), rpos);
            _rifle.Transform = chestT.AffineInverse() * rifleBody;

            // rest on the floor: lowest point exactly at y = 0, hips drift to the node origin
            Transform3D hipsT = _hips.Transform;
            Transform3D spineT = hipsT * _spine.Transform;
            Transform3D chestT2 = spineT * _chest.Transform;
            Transform3D headT = chestT2 * _neck.Transform * _head.Transform;
            float min = 1e9f;
            min = MinY(pb, headT, ProbeHead, min);
            min = MinY(pb, chestT2, ProbeChest, min);
            min = MinY(pb, hipsT, ProbeHips, min);
            min = MinY(pb, hipsT * _thL.Transform, ProbeThighL, min);
            min = MinY(pb, hipsT * _thR.Transform, ProbeThighR, min);
            Transform3D shinL = hipsT * _thL.Transform * _snL.Transform;
            Transform3D shinR = hipsT * _thR.Transform * _snR.Transform;
            min = MinY(pb, shinL, ProbeShin, min);
            min = MinY(pb, shinR, ProbeShin, min);
            min = MinY(pb, shinL * _ftL.Transform, ProbeFoot, min);
            min = MinY(pb, shinR * _ftR.Transform, ProbeFoot, min);
            Transform3D upL = chestT2 * _shL.Transform * _upL.Transform;
            Transform3D upR = chestT2 * _shR.Transform * _upR.Transform;
            min = MinY(pb, upL, ProbeUpper, min);
            min = MinY(pb, upR, ProbeUpper, min);
            Transform3D foL = upL * _foL.Transform;
            Transform3D foR = upR * _foR.Transform;
            min = MinY(pb, foL, ProbeFore, min);
            min = MinY(pb, foR, ProbeFore, min);
            min = MinY(pb, foL * _haL.Transform, ProbeHand, min);
            min = MinY(pb, foR * _haR.Transform, ProbeHand, min);
            min = MinY(pb, rifleBody, ProbeRifle, min);

            float recenter = _pose == 1 ? eb : ea;
            Vector3 hipsRot = pb * hp;
            _pivot.Position = new Vector3(recenter * (DeathShift[_pose * 2] - hipsRot.X), -min + 0.02f, recenter * (DeathShift[_pose * 2 + 1] - hipsRot.Z));

            if (f >= 1f) _settled = true;
        }

        // ------------------------------------------------------------------
        // Construction helpers
        // ------------------------------------------------------------------

        static Node3D Joint(Node3D parent, string name, float x, float y, float z)
        {
            Node3D n = new Node3D();
            n.Name = name;
            n.Position = new Vector3(x, y, z);
            parent.AddChild(n);
            return n;
        }

        static MeshInstance3D Put(Node3D parent, Mesh mesh, Material mat, float px, float py, float pz)
        {
            MeshInstance3D mi = new MeshInstance3D();
            mi.Mesh = mesh;
            mi.MaterialOverride = mat;
            mi.Position = new Vector3(px, py, pz);
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            parent.AddChild(mi);
            return mi;
        }

        static MeshInstance3D Shadow(MeshInstance3D mi)
        {
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            return mi;
        }

        static string F(float v)
        {
            return v.ToString("F3", CultureInfo.InvariantCulture);
        }

        static Mesh BoxMeshOf(float sx, float sy, float sz)
        {
            string key = "b" + F(sx) + "_" + F(sy) + "_" + F(sz);
            Mesh m;
            if (!MeshCache.TryGetValue(key, out m))
            {
                BoxMesh bm = new BoxMesh();
                bm.Size = new Vector3(sx, sy, sz);
                m = bm;
                MeshCache[key] = m;
            }
            return m;
        }

        static MeshInstance3D Box(Node3D parent, Material mat, float sx, float sy, float sz, float px, float py, float pz)
        {
            return Put(parent, BoxMeshOf(sx, sy, sz), mat, px, py, pz);
        }

        static MeshInstance3D BoxR(Node3D parent, Material mat, float sx, float sy, float sz, float px, float py, float pz, float rx, float ry, float rz)
        {
            MeshInstance3D mi = Put(parent, BoxMeshOf(sx, sy, sz), mat, px, py, pz);
            mi.RotationDegrees = new Vector3(rx, ry, rz);
            return mi;
        }

        static MeshInstance3D Cyl(Node3D parent, Material mat, float rTop, float rBot, float h, float px, float py, float pz, float rx, float ry, float rz)
        {
            string key = "c" + F(rTop) + "_" + F(rBot) + "_" + F(h);
            Mesh m;
            if (!MeshCache.TryGetValue(key, out m))
            {
                CylinderMesh cm = new CylinderMesh();
                cm.TopRadius = rTop;
                cm.BottomRadius = rBot;
                cm.Height = h;
                cm.RadialSegments = 8;
                cm.Rings = 1;
                m = cm;
                MeshCache[key] = m;
            }
            MeshInstance3D mi = Put(parent, m, mat, px, py, pz);
            mi.RotationDegrees = new Vector3(rx, ry, rz);
            return mi;
        }

        static MeshInstance3D Sph(Node3D parent, Material mat, float r, float sx, float sy, float sz, float px, float py, float pz)
        {
            string key = "s" + F(r);
            Mesh m;
            if (!MeshCache.TryGetValue(key, out m))
            {
                SphereMesh sm = new SphereMesh();
                sm.Radius = r;
                sm.Height = r * 2f;
                sm.RadialSegments = 12;
                sm.Rings = 7;
                m = sm;
                MeshCache[key] = m;
            }
            MeshInstance3D mi = Put(parent, m, mat, px, py, pz);
            mi.Scale = new Vector3(sx, sy, sz);
            return mi;
        }

        // ------------------------------------------------------------------
        // Materials and camouflage textures
        // ------------------------------------------------------------------

        static StandardMaterial3D Flat(Color c, float rough)
        {
            string key = "f" + c.ToHtml(false) + F(rough);
            StandardMaterial3D m;
            if (!MatCache.TryGetValue(key, out m))
            {
                m = new StandardMaterial3D();
                m.AlbedoColor = c;
                m.Roughness = rough;
                m.Metallic = 0f;
                MatCache[key] = m;
            }
            return m;
        }

        static void EnsureStatics()
        {
            if (_staticsReady) return;
            _gun = Flat(C(30, 33, 32), 0.6f);
            _gunHi = Flat(C(70, 76, 74), 0.5f);
            _strap = Flat(C(38, 40, 34), 0.95f);
            _mask = Flat(C(50, 54, 48), 0.95f);
            _buckle = Flat(C(150, 146, 120), 0.4f);
            _pad = Flat(C(44, 48, 40), 0.8f);
            _webbing = Flat(C(48, 54, 40), 0.95f);
            _belt = Flat(C(42, 40, 34), 0.9f);
            _sole = Flat(C(24, 22, 20), 0.95f);
            _glove = Flat(C(40, 38, 34), 0.9f);
            _optic = Flat(C(24, 26, 26), 0.5f);

            _lens = new StandardMaterial3D();
            _lens.AlbedoColor = C(28, 58, 66);
            _lens.Roughness = 0.2f;
            _lens.EmissionEnabled = true;
            _lens.Emission = new Color(0.2f, 0.6f, 0.65f, 1f);
            _lens.EmissionEnergyMultiplier = 0.5f;

            _flashMat = new StandardMaterial3D();
            _flashMat.AlbedoColor = C(255, 224, 130);
            _flashMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            _flashMat.EmissionEnabled = true;
            _flashMat.Emission = C(255, 224, 130);
            _flashMat.EmissionEnergyMultiplier = 2f;

            for (int i = 0; i < 4; i++)
            {
                StandardMaterial3D s = new StandardMaterial3D();
                s.AlbedoColor = SkinCols[i];
                s.Roughness = 0.85f;
                SkinMats[i] = s;
            }
            _staticsReady = true;
        }

        static PaletteSet GetSet(int pal)
        {
            if (Sets[pal] != null) return Sets[pal];
            PaletteSet ps = new PaletteSet();
            StandardMaterial3D camo = new StandardMaterial3D();
            camo.AlbedoTexture = MakeCamo(pal);
            camo.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
            camo.Roughness = 0.95f;
            camo.Metallic = 0f;
            ps.Camo = camo;
            Color[] g = Gear[pal];
            ps.Vest = Flat(g[0], 0.9f);
            ps.Pouch = Flat(g[1], 0.9f);
            ps.PouchDk = Flat(g[2], 0.9f);
            ps.Helmet = Flat(g[3], 0.85f);
            ps.Boot = Flat(g[4], 0.8f);
            ps.BootHi = Flat(g[5], 0.8f);
            Sets[pal] = ps;
            return ps;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h = h ^ (h >> 16);
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }

        // Tileable value noise on a 32 by 32 grid. The cell size must divide 32.
        static float Noise(int x, int y, int cell, int seed)
        {
            int n = 32 / cell;
            int gx = x / cell, gy = y / cell;
            float fx = (x % cell) / (float)cell;
            float fy = (y % cell) / (float)cell;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(gx % n, gy % n, seed);
            float b = Hash((gx + 1) % n, gy % n, seed);
            float c = Hash(gx % n, (gy + 1) % n, seed);
            float dd = Hash((gx + 1) % n, (gy + 1) % n, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, dd, fx), fy);
        }

        static ImageTexture MakeCamo(int pal)
        {
            Color[] c = CamoCols[pal];
            Image img = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
            int s = pal * 17 + 3;
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    float n = Noise(x, y, 8, s) * 0.65f + Noise(x, y, 4, s + 8) * 0.35f;
                    float m = Noise(x, y, 8, s + 26) * 0.7f + Noise(x, y, 4, s + 38) * 0.3f;
                    Color col;
                    if (n > 0.62f) col = c[3];
                    else if (n > 0.5f) col = c[1];
                    else if (m > 0.55f) col = c[2];
                    else col = c[0];
                    img.SetPixel(x, y, col);
                }
            }
            return ImageTexture.CreateFromImage(img);
        }
    }
}

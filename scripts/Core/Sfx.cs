using System.Collections.Generic;
using Godot;

namespace BunkerBuster
{
    /// <summary>
    /// Plays the game's sound effects (WAVs exported from the browser game's synthesizer).
    /// Keeps a small pool of players so overlapping sounds don't cut each other off.
    /// </summary>
    public partial class Sfx : Node
    {
        static readonly string[] Names =
        {
            "alarm", "alert_spotted", "charge_plant", "collapse_rumble", "debris_thud", "explosion", "fuse_beep",
            "guard_shot", "intel_pickup", "noise_heard", "player_hit", "reload", "rifle_raise", "rifle_shot",
        };
        readonly Dictionary<string, AudioStream> streams = new Dictionary<string, AudioStream>();
        readonly List<AudioStreamPlayer> pool = new List<AudioStreamPlayer>();
        readonly List<AudioStreamPlayer3D> pool3D = new List<AudioStreamPlayer3D>();
        int next, next3D;

        public override void _Ready()
        {
            foreach (var name in Names)
            {
                var s = GD.Load<AudioStream>($"res://assets/audio/{name}.wav");
                if (s != null) streams[name] = s;
            }
            for (int i = 0; i < 12; i++) { var p = new AudioStreamPlayer(); AddChild(p); pool.Add(p); }
            for (int i = 0; i < 16; i++)
            {
                var p = new AudioStreamPlayer3D { UnitSize = 6, MaxDistance = 60 };
                AddChild(p);
                pool3D.Add(p);
            }
        }

        /// <summary>Plays a sound at the listener (volume 0-1, pitch 1 = as recorded).</summary>
        public void Play(string name, float volume = 1, float pitch = 1)
        {
            if (!streams.TryGetValue(name, out var s)) return;
            var p = pool[next];
            next = (next + 1) % pool.Count;
            p.Stream = s;
            p.VolumeDb = Mathf.LinearToDb(Mathf.Max(.001f, volume));
            p.PitchScale = pitch;
            p.Play();
        }

        /// <summary>Plays a sound from a point in the world, so it fades with distance and pans.</summary>
        public void PlayAt(string name, Vector3 at, float volume = 1, float pitch = 1)
        {
            if (!streams.TryGetValue(name, out var s)) return;
            var p = pool3D[next3D];
            next3D = (next3D + 1) % pool3D.Count;
            p.Stream = s;
            p.GlobalPosition = at;
            p.VolumeDb = Mathf.LinearToDb(Mathf.Max(.001f, volume));
            p.PitchScale = pitch;
            p.Play();
        }
    }
}

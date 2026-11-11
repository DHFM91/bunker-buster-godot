using System;
using Godot;

namespace BunkerBuster
{
    /// <summary>A level theme: name, briefing, which skies it can roll, map options and rule tweaks (JS THEMES).</summary>
    public sealed class Theme
    {
        public string Id, Name, Brief;
        public string[] Moods;
        public PlanOptions Plan = new PlanOptions();
        public bool NightVision;
        public double Sight = 1, Hear = 1;
        public int Garrison, Charges = 10;
        public bool StartOnCaution, Moss;

        public static readonly Theme[] All =
        {
            new Theme { Id = "relay", Name = "RELAY STATION", Brief = "A standard concrete relay post. Find the intel, get it back to the pad.",
                Moods = new[] { "day", "overcast", "dusk" } },
            new Theme { Id = "arctic", Name = "ARCTIC OUTPOST", Brief = "Snow on every floor. Your footsteps crunch, so guards hear you from further off. Crouch more.",
                Moods = new[] { "overcast", "day", "night" }, Plan = new PlanOptions { Yards = 2 }, Hear = 1.6 },
            new Theme { Id = "desert", Name = "DESERT DEPOT", Brief = "Sun-baked yards and wide windows. Long sight lines work both ways: guards see further here.",
                Moods = new[] { "day", "smog" }, Plan = new PlanOptions { Yards = 2, Windows = .3 }, Sight = 1.15 },
            new Theme { Id = "night", Name = "NIGHT RAID", Brief = "Lights out, goggles on. Guards can't see as far in the dark, but there are more of them on shift.",
                Moods = new[] { "night" }, NightVision = true, Plan = new PlanOptions { Windows = .2 }, Sight = .72, Garrison = 3 },
            new Theme { Id = "vault", Name = "DEEP VAULT", Brief = "A hardened bunker: reinforced walls everywhere, each needing two charges. You get extra charges.",
                Moods = new[] { "overcast", "smog", "night" }, Plan = new PlanOptions { Reinforced = .5, Windows = .06, Vents = 5 }, Charges = 14 },
            new Theme { Id = "lockdown", Name = "LOCKDOWN", Brief = "They already know something's up: the base starts on CAUTION and the fuel drums are everywhere.",
                Moods = new[] { "dusk", "smog", "night" }, Plan = new PlanOptions { Fuel = .8 }, StartOnCaution = true },
            new Theme { Id = "overgrown", Name = "OVERGROWN DEPOT", Brief = "Abandoned and half reclaimed by moss. Barred gates, windows and crawl vents all over.",
                Moods = new[] { "dusk", "overcast", "day", "smog" }, Plan = new PlanOptions { Vents = 6, Gates = .85, Windows = .28 }, Moss = true },
        };
    }

    /// <summary>Time of day and weather (JS MOODS + the sky half of rollPalette).</summary>
    public sealed class Mood
    {
        public double[] Lo, Hi, Sun;
        public double K, Ambient, Lamps, ElevMin, ElevMax, Clouds;

        public static Mood Get(string id) => id switch
        {
            "dusk" => new Mood { Lo = new[] { 22, .55, .62 }, Hi = new[] { 222, .35, .3 }, Sun = new[] { 1, .66, .4 }, K = 1.05, Ambient = .48, Lamps = 1.1, ElevMin = 22, ElevMax = 34, Clouds = .55 },
            "overcast" => new Mood { Lo = new[] { 205, .06, .66 }, Hi = new[] { 210, .08, .47 }, Sun = new[] { .86, .88, .9 }, K = .42, Ambient = .72, Lamps = .8, ElevMin = 45, ElevMax = 65, Clouds = .95 },
            "smog" => new Mood { Lo = new[] { 50, .28, .6 }, Hi = new[] { 70, .12, .36 }, Sun = new[] { 1, .84, .58 }, K = .85, Ambient = .52, Lamps = .9, ElevMin = 30, ElevMax = 48, Clouds = .7 },
            "night" => new Mood { Lo = new[] { 222, .28, .34 }, Hi = new[] { 230, .36, .16 }, Sun = new[] { .5, .58, .78 }, K = .55, Ambient = .95, Lamps = 1.6, ElevMin = 35, ElevMax = 60, Clouds = .35 },
            _ => new Mood { Lo = new[] { 200, .3, .74 }, Hi = new[] { 212, .5, .46 }, Sun = new[] { 1, .96, .88 }, K = 1.1, Ambient = .58, Lamps = .5, ElevMin = 48, ElevMax = 68, Clouds = .45 },
        };

        /// <summary>JS hsl(h, s, l) as a Godot colour.</summary>
        public static Color Hsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360 / 360;
            double F(double n)
            {
                double k = (n + h * 12) % 12, a = s * Math.Min(l, 1 - l);
                return l - a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1));
            }
            return new Color((float)F(0), (float)F(8), (float)F(4));
        }
    }
}

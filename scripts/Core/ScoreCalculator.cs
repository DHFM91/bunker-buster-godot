using System;
using System.Collections.Generic;
using System.Globalization;

namespace BunkerBuster
{
    /// <summary>One row of the results table (JS: [label, detail, points]).</summary>
    public sealed record ScoreLine(string Label, string Detail, int Points);

    /// <summary>JS scoreRun() return value.</summary>
    public sealed record ScoreResult(
        IReadOnlyList<ScoreLine> Lines, int Subtotal, int Damage, int Penalty, int Total, double Time, bool Won);

    /// <summary>Port of SCORE, scoreRun and fmtTime from bunker-buster.js.</summary>
    public static class ScoreCalculator
    {
        public const int Extract = 5000;
        public const int TimeBase = 480;
        public const int PerSecond = 100;
        public const int Guard = 750;
        public const int Block = 5;

        /// <summary>JS Math.round: halves round toward +infinity.</summary>
        private static int JsRound(double x) => (int)Math.Floor(x + 0.5);

        /// <summary>
        /// JS scoreRun(won) with its inputs made explicit: soldiersDowned = guards with hp 0,
        /// concreteDestroyed = run.blocks, hpRemaining = player.hp, runSeconds = run.time.
        /// </summary>
        public static ScoreResult Calculate(bool won, double runSeconds, int soldiersDowned, int concreteDestroyed, int hpRemaining)
        {
            int damage = 100 - hpRemaining;
            var lines = new List<ScoreLine>();
            if (won)
            {
                lines.Add(new ScoreLine("Extraction", "", Extract));
                lines.Add(new ScoreLine("Time bonus", FormatTime(runSeconds),
                    Math.Max(0, JsRound((TimeBase - runSeconds) * PerSecond))));
            }
            else
            {
                lines.Add(new ScoreLine("Extraction", "failed", 0));
                lines.Add(new ScoreLine("Time bonus", "no extraction", 0));
            }
            lines.Add(new ScoreLine("Soldiers downed", soldiersDowned + " × " + Guard, soldiersDowned * Guard));
            lines.Add(new ScoreLine("Concrete destroyed", concreteDestroyed + " × " + Block, concreteDestroyed * Block));

            int subtotal = 0;
            foreach (var l in lines) subtotal += l.Points;
            int penalty = JsRound(subtotal * (double)Math.Min(100, damage) / 100);
            return new ScoreResult(lines, subtotal, damage, penalty, Math.Max(0, subtotal - penalty), runSeconds, won);
        }

        /// <summary>JS fmtTime(t): "m:ss.ss".</summary>
        public static string FormatTime(double t)
        {
            int m = (int)Math.Floor(t / 60);
            double s = t - m * 60;
            return m.ToString(CultureInfo.InvariantCulture) + ":" + (s < 10 ? "0" : "")
                + s.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}

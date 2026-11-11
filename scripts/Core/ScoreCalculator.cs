using System;
using System.Collections.Generic;
using System.Globalization;

namespace BunkerBuster
{
    /// <summary>One row of the results table (JS: [label, detail, points]).</summary>
    public sealed record ScoreLine(string Label, string Detail, int Points);

    /// <summary>JS scoreRun() return value.</summary>
    public sealed record ScoreResult(
        IReadOnlyList<ScoreLine> Lines, int Subtotal, int Damage, int Cut, int Penalty, double Multiplier, int Base, int Total, double Time, bool Won);

    /// <summary>Port of SCORE, scoreRun (v17 rules) and fmtTime from bunker-buster.js.</summary>
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
        public static ScoreResult Calculate(bool won, double runSeconds, int soldiersDowned, int concreteDestroyed, int hpRemaining, int level = 1)
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
            // damage costs half a percent per HP lost; dying costs a flat 50%, so what you did on the way down still counts
            int cut = won ? Math.Min(50, JsRound(damage * .5)) : 50;
            int penalty = JsRound(subtotal * (double)cut / 100);
            // deeper levels are worth more: +10% per level after the first
            double mult = 1 + .1 * (level - 1);
            int baseScore = Math.Max(0, subtotal - penalty);
            return new ScoreResult(lines, subtotal, damage, cut, penalty, mult, baseScore, JsRound(baseScore * mult), runSeconds, won);
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

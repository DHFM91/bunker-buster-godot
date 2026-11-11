using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BunkerBuster
{
    /// <summary>
    /// Checks the C# map planner against plans produced by the browser game's own code.
    /// Feed it the text of reference/map-plan-reference.json and it reports PASS or FAIL per case.
    /// </summary>
    public static class MapPlanSelfTest
    {
        private sealed class TestCase
        {
            public string Name;
            public uint Seed;
            public PlanOptions Options;

            public TestCase(string name, uint seed, PlanOptions options)
            {
                Name = name;
                Seed = seed;
                Options = options;
            }
        }

        private static List<TestCase> BuildCases()
        {
            var cases = new List<TestCase>();
            foreach (uint seed in new uint[] { 1, 12345, 777, 2026 })
            {
                cases.Add(new TestCase("seed " + seed.ToString(CultureInfo.InvariantCulture) + " default", seed, new PlanOptions()));
            }
            cases.Add(new TestCase("seed 12345 {yards:2}", 12345, new PlanOptions { Yards = 2 }));
            cases.Add(new TestCase("seed 12345 {yards:2, windows:.3}", 12345, new PlanOptions { Yards = 2, Windows = 0.3 }));
            cases.Add(new TestCase("seed 12345 {windows:.2}", 12345, new PlanOptions { Windows = 0.2 }));
            cases.Add(new TestCase("seed 12345 {reinforced:.5, windows:.06, vents:5}", 12345,
                new PlanOptions { Reinforced = 0.5, Windows = 0.06, Vents = 5 }));
            cases.Add(new TestCase("seed 12345 {fuel:.8}", 12345, new PlanOptions { Fuel = 0.8 }));
            cases.Add(new TestCase("seed 12345 {vents:6, gates:.85, windows:.28}", 12345,
                new PlanOptions { Vents = 6, Gates = 0.85, Windows = 0.28 }));
            return cases;
        }

        private static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static void AppendPairs(StringBuilder sb, List<(int A, int B)> list)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(Num(list[i].A)).Append(',').Append(Num(list[i].B)).Append(']');
            }
            sb.Append(']');
        }

        /// <summary>Writes a plan in the canonical compact JSON shape shared with the reference script.</summary>
        private static string Canonical(LayoutPlan plan)
        {
            var sb = new StringBuilder();
            sb.Append("{\"passages\":");
            AppendPairs(sb, plan.Passages);

            sb.Append(",\"spaces\":[");
            for (int i = 0; i < plan.Spaces.Count; i++)
            {
                Space s = plan.Spaces[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"kind\":\"").Append(s.Kind).Append('"');
                sb.Append(",\"col\":").Append(Num(s.Col));
                sb.Append(",\"row\":").Append(Num(s.Row));
                sb.Append(",\"w\":").Append(Num(s.W));
                sb.Append(",\"h\":").Append(Num(s.H));
                if (s.Kind == "yard")
                {
                    // The shift is a multiple of 2^-32, so this integer is exact.
                    long shiftBits = (long)Math.Round(s.TruckShift * 4294967296.0);
                    sb.Append(",\"along\":\"").Append(s.Along).Append('"');
                    sb.Append(",\"shiftBits\":").Append(shiftBits.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"cover\":").Append(Num(s.Cover));
                }
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"owner\":[");
            for (int i = 0; i < plan.Owner.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Num(plan.Owner[i]));
            }
            sb.Append(']');

            sb.Append(",\"barred\":");
            AppendPairs(sb, plan.Barred);
            sb.Append(",\"vents\":");
            AppendPairs(sb, plan.Vents);
            sb.Append(",\"reinforced\":");
            AppendPairs(sb, plan.Reinforced);

            sb.Append(",\"windows\":[");
            for (int i = 0; i < plan.Windows.Count; i++)
            {
                var w = plan.Windows[i];
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(Num(w.A)).Append(',').Append(Num(w.B)).Append(',').Append(Bool(w.Barred)).Append(']');
            }
            sb.Append(']');

            sb.Append(",\"posts\":[");
            for (int i = 0; i < plan.Posts.Count; i++)
            {
                var p = plan.Posts[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"cell\":").Append(Num(p.Cell)).Append(",\"facing\":").Append(Num(p.Facing)).Append('}');
            }
            sb.Append(']');

            sb.Append(",\"props\":[");
            for (int i = 0; i < plan.Props.Count; i++)
            {
                Prop q = plan.Props[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"type\":\"").Append(q.Type).Append("\",\"cell\":").Append(Num(q.Cell));
                if (q.Type == "lamp" || q.Type == "sandbags")
                {
                    sb.Append(",\"side\":").Append(Num(q.Side));
                }
                else if (q.Type == "crates")
                {
                    sb.Append(",\"corner\":").Append(Num(q.Corner));
                }
                else
                {
                    sb.Append(",\"corner\":").Append(Num(q.Corner)).Append(",\"fuel\":").Append(Bool(q.Fuel));
                }
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"reach\":").Append(Num(plan.Reach));
            sb.Append('}');
            return sb.ToString();
        }

        private static string Excerpt(string text, int index)
        {
            int start = Math.Max(0, index - 30);
            int length = Math.Min(70, text.Length - start);
            return text.Substring(start, length);
        }

        /// <summary>
        /// Runs every case and returns a report with one line per case plus a summary line.
        /// </summary>
        public static string Run(string referenceJsonText)
        {
            var report = new StringBuilder();
            JsonArray reference;
            try
            {
                reference = JsonNode.Parse(referenceJsonText) as JsonArray;
            }
            catch (Exception ex)
            {
                return "FAIL could not read reference JSON: " + ex.Message;
            }
            if (reference == null) return "FAIL reference JSON is not an array";

            var expectedByName = new Dictionary<string, string>();
            foreach (JsonNode entry in reference)
            {
                if (entry == null) continue;
                string name = (string)entry["name"];
                JsonNode planNode = entry["plan"];
                if (name != null && planNode != null) expectedByName[name] = planNode.ToJsonString();
            }

            int passed = 0;
            int failed = 0;
            foreach (TestCase testCase in BuildCases())
            {
                if (!expectedByName.TryGetValue(testCase.Name, out string expected))
                {
                    report.AppendLine("FAIL " + testCase.Name + ": no matching case in reference");
                    failed++;
                    continue;
                }

                string actual;
                try
                {
                    var rng = new Mulberry32(testCase.Seed);
                    LayoutPlan plan = MapPlan.Plan(rng.AsFunc(), testCase.Options);
                    actual = JsonNode.Parse(Canonical(plan)).ToJsonString();
                }
                catch (Exception ex)
                {
                    report.AppendLine("FAIL " + testCase.Name + ": exception " + ex.Message);
                    failed++;
                    continue;
                }

                if (actual == expected)
                {
                    report.AppendLine("PASS " + testCase.Name);
                    passed++;
                    continue;
                }

                int diff = 0;
                int shared = Math.Min(actual.Length, expected.Length);
                while (diff < shared && actual[diff] == expected[diff]) diff++;
                report.AppendLine("FAIL " + testCase.Name + ": first difference at character " + Num(diff));
                report.AppendLine("  expected: ..." + Excerpt(expected, diff));
                report.AppendLine("  actual:   ..." + Excerpt(actual, diff));
                failed++;
            }

            report.AppendLine(Num(passed) + " passed, " + Num(failed) + " failed");
            return report.ToString();
        }
    }
}

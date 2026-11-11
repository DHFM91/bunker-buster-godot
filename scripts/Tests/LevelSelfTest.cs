using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BunkerBuster
{
    /// <summary>
    /// Checks the C# level stamping against levels produced by the browser game's own reset() code.
    /// Feed it the text of reference/level-reference.json and it reports PASS or FAIL per case.
    /// </summary>
    public static class LevelSelfTest
    {
        private static readonly string[] ThemeIds = { "relay", "desert", "overgrown" };
        private static readonly uint[] Seeds = { 1, 12345, 777, 2026 };

        private static string Num(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>FNV-1a, 32 bit, over the voxel bytes.</summary>
        private static uint Hash(byte[] data)
        {
            uint h = 2166136261u;
            unchecked
            {
                for (int i = 0; i < data.Length; i++)
                {
                    h ^= data[i];
                    h *= 16777619u;
                }
            }
            return h;
        }

        private static Theme FindTheme(string id)
        {
            foreach (Theme theme in Theme.All)
            {
                if (theme.Id == id) return theme;
            }
            return null;
        }

        private static bool Same(double actual, double expected)
        {
            return Math.Abs(actual - expected) < 1e-9;
        }

        /// <summary>Compares one row of numbers; returns null when equal, else a description.</summary>
        private static string CompareRow(string what, int index, double[] actual, JsonNode expected)
        {
            JsonArray row = expected as JsonArray;
            if (row == null || row.Count != actual.Length)
            {
                return what + " " + Num(index) + ": wrong shape in reference";
            }
            for (int i = 0; i < actual.Length; i++)
            {
                double want = row[i].GetValue<double>();
                if (!Same(actual[i], want))
                {
                    return what + " " + Num(index) + " field " + Num(i) + ": expected " + Num(want) + " got " + Num(actual[i]);
                }
            }
            return null;
        }

        private static string CheckCase(JsonNode entry, Theme theme, uint seed)
        {
            var world = new VoxelWorld();
            Level level = LevelBuilder.Build(world, theme, new Mulberry32(seed).AsFunc());

            double wantHash = entry["hash"].GetValue<double>();
            uint hash = Hash(world.Data);
            if (!Same(hash, wantHash))
            {
                return "voxel hash expected " + Num(wantHash) + " got " + Num(hash);
            }

            var counts = new int[16];
            foreach (byte v in world.Data) counts[v & 15]++;
            JsonArray wantCounts = entry["counts"] as JsonArray;
            if (wantCounts == null || wantCounts.Count != counts.Length) return "counts: wrong shape in reference";
            for (int i = 0; i < counts.Length; i++)
            {
                if (!Same(counts[i], wantCounts[i].GetValue<double>()))
                {
                    return "material " + Num(i) + " count expected " + Num(wantCounts[i].GetValue<double>()) + " got " + Num(counts[i]);
                }
            }

            JsonArray wantLamps = entry["lamps"] as JsonArray;
            if (wantLamps == null || wantLamps.Count != level.Lamps.Count)
            {
                return "lamp count expected " + Num(wantLamps == null ? -1 : wantLamps.Count) + " got " + Num(level.Lamps.Count);
            }
            for (int i = 0; i < level.Lamps.Count; i++)
            {
                Lamp l = level.Lamps[i];
                var row = new double[]
                {
                    Math.Round(l.X, 3), Math.Round(l.Y, 3), Math.Round(l.Z, 3),
                    Math.Round(l.Nx, 3), Math.Round(l.Nz, 3), Math.Round(l.Range, 3), Math.Round(l.Power, 3),
                };
                string problem = CompareRow("lamp", i, row, wantLamps[i]);
                if (problem != null) return problem;
            }

            JsonArray wantSentries = entry["sentries"] as JsonArray;
            if (wantSentries == null || wantSentries.Count != level.Sentries.Count)
            {
                return "sentry count expected " + Num(wantSentries == null ? -1 : wantSentries.Count) + " got " + Num(level.Sentries.Count);
            }
            for (int i = 0; i < level.Sentries.Count; i++)
            {
                Sentry s = level.Sentries[i];
                var row = new double[] { Math.Round(s.X, 3), Math.Round(s.Z, 3), s.Cell, Math.Round(s.Yaw, 4) };
                string problem = CompareRow("sentry", i, row, wantSentries[i]);
                if (problem != null) return problem;
            }
            return null;
        }

        /// <summary>Runs every case and returns a report with one line per case plus a summary line.</summary>
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

            var byName = new Dictionary<string, JsonNode>();
            foreach (JsonNode entry in reference)
            {
                if (entry == null) continue;
                string name = (string)entry["name"];
                if (name != null) byName[name] = entry;
            }

            int passed = 0;
            int failed = 0;
            foreach (string themeId in ThemeIds)
            {
                foreach (uint seed in Seeds)
                {
                    string name = themeId + " seed " + seed.ToString(CultureInfo.InvariantCulture);
                    Theme theme = FindTheme(themeId);
                    if (theme == null)
                    {
                        report.AppendLine("FAIL " + name + ": theme not found");
                        failed++;
                        continue;
                    }
                    if (!byName.TryGetValue(name, out JsonNode entry))
                    {
                        report.AppendLine("FAIL " + name + ": no matching case in reference");
                        failed++;
                        continue;
                    }

                    string problem;
                    try
                    {
                        problem = CheckCase(entry, theme, seed);
                    }
                    catch (Exception ex)
                    {
                        problem = "exception " + ex.Message;
                    }

                    if (problem == null)
                    {
                        report.AppendLine("PASS " + name);
                        passed++;
                    }
                    else
                    {
                        report.AppendLine("FAIL " + name + ": " + problem);
                        failed++;
                    }
                }
            }

            report.AppendLine(passed.ToString(CultureInfo.InvariantCulture) + " passed, " + failed.ToString(CultureInfo.InvariantCulture) + " failed");
            return report.ToString();
        }
    }
}

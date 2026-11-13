using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace BunkerBuster
{
    /// <summary>One leaderboard row: initials, run total, seconds played, levels cleared.</summary>
    public sealed class ScoreEntry
    {
        public string Name;
        public int Score;
        public double Time;
        public int Levels;
    }

    /// <summary>
    /// Global top-10 kept in a private JSON bin on extendsclass.com. The bin needs its own
    /// security key to read or write; the game loads that key from res://leaderboard.key, a local
    /// file that is packed into exported builds but never committed (see .gitignore). With no key
    /// file the board simply runs offline.
    /// Rows are stored as [initials, score, seconds, levels], the same shape as the browser game.
    /// </summary>
    public partial class Leaderboard : Node
    {
        public const string Url = "https://json.extendsclass.com/bin/b83713eff12d";
        public const int Size = 10;

        public List<ScoreEntry> Board = new List<ScoreEntry>();
        public bool Online;
        string key;

        public override void _Ready()
        {
            if (FileAccess.FileExists("res://leaderboard.key"))
                key = FileAccess.GetFileAsString("res://leaderboard.key").Trim();
        }

        public bool HasKey => !string.IsNullOrEmpty(key);

        public bool Qualifies(int score) => score > 0 && (Board.Count < Size || score > Board[Board.Count - 1].Score);

        async Task<(bool Ok, string Body)> Request(HttpClient.Method method, string body)
        {
            if (!HasKey) return (false, null);
            var http = new HttpRequest { Timeout = 8 };
            AddChild(http);
            try
            {
                string[] headers = { "Content-Type: application/json", "Security-key: " + key, "Cache-Control: no-cache" };
                var err = http.Request(Url, headers, method, body ?? "");
                if (err != Error.Ok) return (false, null);
                var result = await ToSignal(http, HttpRequest.SignalName.RequestCompleted);
                long code = result[1].AsInt64();
                string text = System.Text.Encoding.UTF8.GetString(result[3].AsByteArray());
                return (code >= 200 && code < 300, text);
            }
            catch (Exception) { return (false, null); }
            finally { http.QueueFree(); }
        }

        static List<ScoreEntry> Parse(string text)
        {
            var list = new List<ScoreEntry>();
            if (string.IsNullOrEmpty(text)) return list;
            // the bin host sometimes tacks a web page onto the end of the reply: keep only the JSON part
            int cut = text.IndexOf('<');
            if (cut >= 0) text = text.Substring(0, cut);
            if (JsonNode.Parse(text) is not JsonArray rows) return list;
            foreach (var row in rows)
            {
                if (row is not JsonArray r || r.Count < 2) continue;
                try
                {
                    string name = new string((r[0]?.GetValue<string>() ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).Take(3).ToArray());
                    list.Add(new ScoreEntry
                    {
                        Name = name.Length > 0 ? name : "???",
                        Score = Math.Max(0, (int)Math.Round(r[1].GetValue<double>())),
                        Time = r.Count > 2 && r[2] != null ? r[2].GetValue<double>() : 0,
                        Levels = r.Count > 3 && r[3] != null ? Math.Clamp((int)Math.Round(r[3].GetValue<double>()), 0, 999) : 0,
                    });
                }
                catch (Exception) { }
            }
            return list.OrderByDescending(e => e.Score).Take(Size).ToList();
        }

        static string Serialize(List<ScoreEntry> list) =>
            "[" + string.Join(",", list.Select(e =>
                $"[{JsonSerializer.Serialize(e.Name)},{e.Score},{Math.Round(e.Time, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)},{e.Levels}]")) + "]";

        /// <summary>Loads the current board. Returns false (and keeps the last known board) when offline.</summary>
        public async Task<bool> Fetch()
        {
            var (ok, body) = await Request(HttpClient.Method.Get, null);
            if (ok)
            {
                try { Board = Parse(body); Online = true; return true; }
                catch (Exception) { }
            }
            Online = false;
            return false;
        }

        /// <summary>Overwrites the whole board (used by the automated test to put the board back as it was).</summary>
        public async Task<bool> Replace(List<ScoreEntry> list)
        {
            var (ok, _) = await Request(HttpClient.Method.Put, Serialize(list));
            if (ok) Board = list.ToList();
            return ok;
        }

        /// <summary>Adds an entry: re-reads the board right before writing, merges, and saves the top 10.</summary>
        public async Task<bool> Submit(ScoreEntry entry)
        {
            await Fetch();
            var merged = Board.Concat(new[] { entry }).OrderByDescending(e => e.Score).Take(Size).ToList();
            Board = merged;
            if (!Online) return false;
            var (ok, _) = await Request(HttpClient.Method.Put, Serialize(merged));
            Online = ok;
            return ok;
        }
    }
}

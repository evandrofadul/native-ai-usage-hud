using System.Globalization;
using System.Text.Json;

namespace AiUsageHud.Core.TokenTracking;

/// <summary>
/// Reads <em>all</em> Gemini CLI chat transcripts across every project
/// (<c>~/.gemini/tmp/&lt;project&gt;/chats/**/*.jsonl</c>) and aggregates the richer
/// statistics shown on the dashboard card: session count, message count, total
/// tokens, active days, streaks, the busiest hour, the favourite model and a
/// per-day activity series for the heatmap.
///
/// Each file is one session (its id lives in the opening <c>sessionId</c> meta line).
/// Turns are the top-level <c>type:"user"</c> / <c>type:"gemini"</c> lines; the model
/// and per-turn <c>tokens</c> block live on the gemini line. The totalizer counts
/// input + output (+ reasoning), excluding the cached portion.
///
/// This is the Gemini sibling of <see cref="ClaudeUsageStatsReader"/> /
/// <see cref="CodexUsageStatsReader"/> and shares the streak/heatmap math in
/// <see cref="UsageDayStats"/>. Parsed files are cached by length+mtime.
/// </summary>
public sealed class GeminiUsageStatsReader
{
    /// <summary>Trailing days the heatmap covers — see <see cref="UsageDayStats.HeatmapDays"/>.</summary>
    public static int HeatmapDays => UsageDayStats.HeatmapDays;

    private readonly string _tmpDir;
    private readonly Dictionary<string, FileStats> _cache = new();

    public GeminiUsageStatsReader(string? tmpDir = null)
    {
        _tmpDir = tmpDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gemini", "tmp");
    }

    /// <summary>Aggregate stats across the entire history, or null when nothing is available.</summary>
    public VendorUsageStats? Read()
    {
        if (!Directory.Exists(_tmpDir)) return null;

        string[] all;
        try { all = Directory.GetFiles(_tmpDir, "*.jsonl", SearchOption.AllDirectories); }
        catch { return null; }
        if (all.Length == 0) return null;

        // The (most recently touched) active project still labels the card's Project.
        var latest = all.MaxBy(SafeMtime)!;
        var rel = Path.GetRelativePath(_tmpDir, latest);
        var project = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

        long totalTokens = 0, messages = 0;
        var byDay = new Dictionary<DateOnly, int>();
        var byHour = new int[24];
        var byModel = new Dictionary<string, int>();
        var sessions = new HashSet<string>();

        lock (_cache)
        {
            foreach (var f in all)
            {
                var s = Parse(f);
                totalTokens += s.Tokens;
                messages += s.Messages;
                foreach (var (day, n) in s.ByDay)
                    byDay[day] = byDay.GetValueOrDefault(day) + n;
                for (var h = 0; h < 24; h++) byHour[h] += s.ByHour[h];
                foreach (var (model, n) in s.ByModel)
                    byModel[model] = byModel.GetValueOrDefault(model) + n;
                if (s.SessionId is { Length: > 0 } id) sessions.Add(id);
            }
        }

        int? peakHour = messages > 0 ? Array.IndexOf(byHour, byHour.Max()) : null;
        var favorite = byModel.Count > 0
            ? ModelNames.Friendly(byModel.MaxBy(kv => kv.Value).Key)
            : null;

        return new VendorUsageStats(
            Project: project,
            Sessions: sessions.Count > 0 ? sessions.Count : all.Length,
            Messages: messages,
            TotalTokens: totalTokens,
            ActiveDays: byDay.Count,
            CurrentStreak: UsageDayStats.CurrentStreak(byDay.Keys),
            LongestStreak: UsageDayStats.LongestStreak(byDay.Keys),
            PeakHour: peakHour,
            FavoriteModel: favorite,
            Heatmap: UsageDayStats.BuildHeatmap(byDay));
    }

    private static long SafeMtime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path).Ticks; } catch { return 0; }
    }

    private FileStats Parse(string path)
    {
        FileInfo fi;
        try { fi = new FileInfo(path); } catch { return FileStats.Empty; }

        if (_cache.TryGetValue(path, out var cached)
            && cached.Length == fi.Length && cached.Mtime == fi.LastWriteTimeUtc)
            return cached;

        long tokens = 0, messages = 0;
        var byDay = new Dictionary<DateOnly, int>();
        var byHour = new int[24];
        var byModel = new Dictionary<string, int>();
        string? sessionId = null;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length < 12) continue;
                var hasType = line.Contains("\"type\"", StringComparison.Ordinal);
                if (!hasType && !line.Contains("\"sessionId\"", StringComparison.Ordinal)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    if (sessionId is null && root.TryGetProperty("sessionId", out var sid)
                        && sid.ValueKind == JsonValueKind.String)
                        sessionId = sid.GetString();

                    var type = root.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String
                        ? ty.GetString() : null;
                    if (type is not ("user" or "gemini")) continue;

                    messages++;

                    if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
                    {
                        var local = when.ToLocalTime();
                        var day = DateOnly.FromDateTime(local.DateTime);
                        byDay[day] = byDay.GetValueOrDefault(day) + 1;
                        byHour[local.Hour]++;
                    }

                    if (type != "gemini") continue;

                    if (root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                        && m.GetString() is { Length: > 0 } model)
                        byModel[model] = byModel.GetValueOrDefault(model) + 1;

                    // Totalizer excludes the cached portion (input already includes it).
                    if (root.TryGetProperty("tokens", out var tk) && tk.ValueKind == JsonValueKind.Object)
                    {
                        var input = Long(tk, "input");
                        var cachedIn = Long(tk, "cached");
                        tokens += Math.Max(input - cachedIn, 0) + Long(tk, "output") + Long(tk, "thoughts") + Long(tk, "tool");
                    }
                }
                catch { continue; }
            }
        }
        catch { /* unreadable file — treat as empty */ }

        var stats = new FileStats(fi.Length, fi.LastWriteTimeUtc, tokens, messages, byDay, byHour, byModel, sessionId);
        _cache[path] = stats;
        return stats;
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private sealed record FileStats(
        long Length, DateTime Mtime, long Tokens, long Messages,
        Dictionary<DateOnly, int> ByDay, int[] ByHour,
        Dictionary<string, int> ByModel, string? SessionId)
    {
        public static readonly FileStats Empty =
            new(0, default, 0, 0, new(), new int[24], new(), null);
    }
}

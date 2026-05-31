using System.Globalization;
using System.Text.Json;

namespace AiUsageBar.Core.TokenTracking;

/// <summary>
/// Reads <em>all</em> Claude Code session transcripts across every project
/// (<c>~/.claude/projects/&lt;project&gt;/*.jsonl</c>) and aggregates the richer
/// statistics shown on the dashboard card: session count, message count, total
/// tokens, active days, streaks, the busiest hour, the favourite model and a
/// per-day activity series for the heatmap. The dashboard reflects the entire
/// history, not a single project or session.
///
/// The totalizer (total tokens) counts input + output only; cache tokens are
/// excluded because they are billed separately.
///
/// This is a sibling of <see cref="ClaudeTokenReader"/> (which sums tokens for the
/// active project's usage section). Parsed files are cached by length+mtime so
/// unchanged sessions are never re-read.
/// </summary>
public sealed class ClaudeUsageStatsReader
{
    /// <summary>Trailing days the heatmap covers — see <see cref="UsageDayStats.HeatmapDays"/>.</summary>
    public static int HeatmapDays => UsageDayStats.HeatmapDays;

    private readonly string _projectsDir;
    private readonly Dictionary<string, FileStats> _cache = new();

    public ClaudeUsageStatsReader(string? projectsDir = null)
    {
        _projectsDir = projectsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
    }

    /// <summary>Aggregate stats across the entire history, or null when nothing is available.</summary>
    public VendorUsageStats? Read()
    {
        if (!Directory.Exists(_projectsDir)) return null;

        string[] all;
        try { all = Directory.GetFiles(_projectsDir, "*.jsonl", SearchOption.AllDirectories); }
        catch { return null; }
        if (all.Length == 0) return null;

        // Entire history: the dashboard aggregates every project's transcripts, not
        // just the active one. The (most recently touched) active project still labels
        // the card's Project field.
        var latest = all.MaxBy(SafeMtime)!;
        var rel = Path.GetRelativePath(_projectsDir, latest);
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
                foreach (var id in s.Sessions) sessions.Add(id);
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
        var sessions = new HashSet<string>();

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length < 12 || !line.Contains("\"type\"", StringComparison.Ordinal)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("sessionId", out var sid) && sid.ValueKind == JsonValueKind.String)
                        sessions.Add(sid.GetString()!);

                    var type = root.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String
                        ? ty.GetString() : null;
                    if (type is not ("user" or "assistant")) continue;

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

                    if (type == "assistant" && root.TryGetProperty("message", out var msg)
                        && msg.ValueKind == JsonValueKind.Object)
                    {
                        var model = msg.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                            ? m.GetString() ?? "" : "";
                        if (model is not ("" or "<synthetic>"))
                            byModel[model] = byModel.GetValueOrDefault(model) + 1;

                        // Totalizer excludes cache tokens (cache is billed separately).
                        if (msg.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                            tokens += Long(usage, "input_tokens") + Long(usage, "output_tokens");
                    }
                }
                catch { continue; }
            }
        }
        catch { /* unreadable file — treat as empty */ }

        var stats = new FileStats(fi.Length, fi.LastWriteTimeUtc, tokens, messages, byDay, byHour, byModel, sessions);
        _cache[path] = stats;
        return stats;
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private sealed record FileStats(
        long Length, DateTime Mtime, long Tokens, long Messages,
        Dictionary<DateOnly, int> ByDay, int[] ByHour,
        Dictionary<string, int> ByModel, HashSet<string> Sessions)
    {
        public static readonly FileStats Empty =
            new(0, default, 0, 0, new(), new int[24], new(), new());
    }
}

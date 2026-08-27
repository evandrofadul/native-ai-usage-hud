using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiUsageHud.Core.TokenTracking;

/// <summary>
/// Reads <em>all</em> Antigravity (agy CLI / IDE / 2.0) conversation transcripts
/// (<c>~/.gemini/antigravity-cli/brain/&lt;conversation-id&gt;/.system_generated/logs/transcript.jsonl</c>)
/// and aggregates the richer statistics shown on the dashboard card: session count,
/// message count, active days, streaks, the busiest hour, the favourite model and a
/// per-day activity series for the heatmap.
///
/// Each transcript directory is one conversation (session). Message turns are the
/// lines with <c>type == "USER_INPUT"</c> or <c>type == "PLANNER_RESPONSE"</c>.
/// The model in use is inferred from the last <c>USER_SETTINGS_CHANGE</c> block
/// embedded in the <c>USER_INPUT</c> content. Token counts are not available in
/// the Antigravity transcript format, so <see cref="VendorUsageStats.TotalTokens"/>
/// is always zero.
///
/// This is the Antigravity sibling of <see cref="ClaudeUsageStatsReader"/> /
/// <see cref="CodexUsageStatsReader"/> / <see cref="GeminiUsageStatsReader"/> and
/// shares the streak/heatmap math in <see cref="UsageDayStats"/>. Parsed files are
/// cached by length+mtime so unchanged transcripts are never re-read.
/// </summary>
public sealed partial class AntigravityUsageStatsReader
{
    /// <summary>Trailing days the heatmap covers — see <see cref="UsageDayStats.HeatmapDays"/>.</summary>
    public static int HeatmapDays => UsageDayStats.HeatmapDays;

    private readonly string _brainDir;
    private readonly Dictionary<string, FileStats> _cache = new();

    public AntigravityUsageStatsReader(string? brainDir = null)
    {
        _brainDir = brainDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gemini", "antigravity-cli", "brain");
    }

    /// <summary>Aggregate stats across the entire history, or null when nothing is available.</summary>
    public VendorUsageStats? Read()
    {
        if (!Directory.Exists(_brainDir)) return null;

        string[] all;
        try
        {
            all = Directory.GetDirectories(_brainDir)
                .Select(d => Path.Combine(d, ".system_generated", "logs", "transcript.jsonl"))
                .Where(File.Exists)
                .ToArray();
        }
        catch { return null; }
        if (all.Length == 0) return null;

        // The most recently touched transcript labels the card's Project.
        var latest = MostRecentFile.Pick(all)!;
        var conversationDir = Path.GetFileName(Path.GetDirectoryName(
            Path.GetDirectoryName(Path.GetDirectoryName(latest))));

        long messages = 0;
        var byDay = new Dictionary<DateOnly, int>();
        var byHour = new int[24];
        var byModel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sessions = 0;

        lock (_cache)
        {
            foreach (var f in all)
            {
                var s = Parse(f);
                messages += s.Messages;
                foreach (var (day, n) in s.ByDay)
                    byDay[day] = byDay.GetValueOrDefault(day) + n;
                for (var h = 0; h < 24; h++) byHour[h] += s.ByHour[h];
                if (s.Model is { Length: > 0 } model)
                    byModel[model] = byModel.GetValueOrDefault(model) + 1;
                sessions++;
            }
        }

        int? peakHour = messages > 0 ? Array.IndexOf(byHour, byHour.Max()) : null;
        var favorite = byModel.Count > 0 ? byModel.MaxBy(kv => kv.Value).Key : null;

        return new VendorUsageStats(
            Project: conversationDir ?? "Antigravity",
            Sessions: sessions,
            Messages: messages,
            TotalTokens: 0,
            ActiveDays: byDay.Count,
            CurrentStreak: UsageDayStats.CurrentStreak(byDay.Keys),
            LongestStreak: UsageDayStats.LongestStreak(byDay.Keys),
            PeakHour: peakHour,
            FavoriteModel: favorite,
            Heatmap: UsageDayStats.BuildHeatmap(byDay));
    }

    private FileStats Parse(string path)
    {
        FileInfo fi;
        try { fi = new FileInfo(path); } catch { return FileStats.Empty; }

        if (_cache.TryGetValue(path, out var cached)
            && cached.Length == fi.Length && cached.Mtime == fi.LastWriteTimeUtc)
            return cached;

        long messages = 0;
        var byDay = new Dictionary<DateOnly, int>();
        var byHour = new int[24];
        string? model = null;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length < 12 || !line.Contains("\"type\"", StringComparison.Ordinal)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var type = root.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String
                        ? ty.GetString() : null;

                    // Extract model from USER_SETTINGS_CHANGE in content
                    if (type == "USER_INPUT"
                        && root.TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.String)
                    {
                        var text = content.GetString() ?? "";
                        var m = ModelSelectionPattern().Match(text);
                        if (m.Success)
                            model = m.Groups[1].Value.Trim().TrimEnd('.');
                    }

                    if (type is not ("USER_INPUT" or "PLANNER_RESPONSE")) continue;

                    messages++;

                    if (root.TryGetProperty("created_at", out var ts) && ts.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
                    {
                        var local = when.ToLocalTime();
                        var day = DateOnly.FromDateTime(local.DateTime);
                        byDay[day] = byDay.GetValueOrDefault(day) + 1;
                        byHour[local.Hour]++;
                    }
                }
                catch { continue; }
            }
        }
        catch { /* unreadable file — treat as empty */ }

        var stats = new FileStats(fi.Length, fi.LastWriteTimeUtc, messages, byDay, byHour, model);
        _cache[path] = stats;
        return stats;
    }

    /// <summary>Extracts the model name from a <c>USER_SETTINGS_CHANGE</c> block.</summary>
    [GeneratedRegex(@"Model Selection.+?to (.+?)[\.\s]", RegexOptions.Singleline)]
    private static partial Regex ModelSelectionPattern();

    private sealed record FileStats(
        long Length, DateTime Mtime, long Messages,
        Dictionary<DateOnly, int> ByDay, int[] ByHour, string? Model)
    {
        public static readonly FileStats Empty =
            new(0, default, 0, new(), new int[24], null);
    }
}

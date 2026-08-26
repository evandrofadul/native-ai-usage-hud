using System.Globalization;
using System.Text.Json;

namespace AiUsageHud.Core.TokenTracking;

/// <summary>
/// Reads <em>all</em> Codex CLI session rollouts across the history
/// (<c>~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl</c>) and aggregates the richer
/// statistics shown on the dashboard card: session count, message count, total
/// tokens, active days, streaks, the busiest hour, the favourite model and a
/// per-day activity series for the heatmap.
///
/// Each rollout is one session (its id lives in the opening <c>session_meta</c>
/// line). Conversation turns are the <c>event_msg</c> lines of type
/// <c>user_message</c> / <c>agent_message</c>; the model comes from the preceding
/// <c>turn_context</c>. The token totalizer is the session's <em>last</em>
/// <c>token_count</c> running total, counting input + output only (Codex's
/// <c>input_tokens</c> already includes the cached portion, which is excluded).
///
/// This is the OpenAI sibling of <see cref="ClaudeUsageStatsReader"/> and shares the
/// streak/heatmap math in <see cref="UsageDayStats"/>. Parsed files are cached by
/// length+mtime so unchanged rollouts are never re-read.
/// </summary>
public sealed class CodexUsageStatsReader
{
    /// <summary>Trailing days the heatmap covers — see <see cref="UsageDayStats.HeatmapDays"/>.</summary>
    public static int HeatmapDays => UsageDayStats.HeatmapDays;

    private readonly string _sessionsDir;
    private readonly Dictionary<string, FileStats> _cache = new();

    public CodexUsageStatsReader(string? sessionsDir = null)
    {
        _sessionsDir = sessionsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex", "sessions");
    }

    /// <summary>Aggregate stats across the entire history, or null when nothing is available.</summary>
    public VendorUsageStats? Read()
    {
        if (!Directory.Exists(_sessionsDir)) return null;

        string[] all;
        try { all = Directory.GetFiles(_sessionsDir, "*.jsonl", SearchOption.AllDirectories); }
        catch { return null; }
        if (all.Length == 0) return null;

        // The (most recently touched) active workspace still labels the card's Project.
        var active = MostRecentFile.Pick(all)!;

        long totalTokens = 0, messages = 0;
        var byDay = new Dictionary<DateOnly, int>();
        var byHour = new int[24];
        var byModel = new Dictionary<string, int>();
        var sessions = new HashSet<string>();
        string? activeCwd;

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

            activeCwd = Parse(active).Cwd;
        }

        int? peakHour = messages > 0 ? Array.IndexOf(byHour, byHour.Max()) : null;
        var favorite = byModel.Count > 0
            ? ModelNames.Friendly(byModel.MaxBy(kv => kv.Value).Key)
            : null;

        return new VendorUsageStats(
            Project: string.IsNullOrEmpty(activeCwd) ? "Codex" : activeCwd!,
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

    private FileStats Parse(string path)
    {
        FileInfo fi;
        try { fi = new FileInfo(path); } catch { return FileStats.Empty; }

        if (_cache.TryGetValue(path, out var cached)
            && cached.Length == fi.Length && cached.Mtime == fi.LastWriteTimeUtc)
            return cached;

        long lastTokens = 0, messages = 0;
        var byDay = new Dictionary<DateOnly, int>();
        var byHour = new int[24];
        var byModel = new Dictionary<string, int>();
        string? sessionId = null, cwd = null, currentModel = null;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                // Cheap pre-filter: skip the bulk response_item lines (reasoning,
                // function calls, …) we don't aggregate.
                if (line.Length < 12) continue;
                if (!line.Contains("session_meta", StringComparison.Ordinal)
                    && !line.Contains("turn_context", StringComparison.Ordinal)
                    && !line.Contains("token_count", StringComparison.Ordinal)
                    && !line.Contains("event_msg", StringComparison.Ordinal)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String
                        ? ty.GetString() : null;
                    if (!root.TryGetProperty("payload", out var payload)
                        || payload.ValueKind != JsonValueKind.Object) continue;

                    switch (type)
                    {
                        case "session_meta":
                            if (sessionId is null && payload.TryGetProperty("id", out var idEl)
                                && idEl.ValueKind == JsonValueKind.String)
                                sessionId = idEl.GetString();
                            if (cwd is null && payload.TryGetProperty("cwd", out var cwdEl)
                                && cwdEl.ValueKind == JsonValueKind.String)
                                cwd = cwdEl.GetString();
                            continue;

                        case "turn_context":
                            if (payload.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String)
                                currentModel = mEl.GetString();
                            if (cwd is null && payload.TryGetProperty("cwd", out var tcwd)
                                && tcwd.ValueKind == JsonValueKind.String)
                                cwd = tcwd.GetString();
                            continue;
                    }

                    var pType = payload.TryGetProperty("type", out var pt) && pt.ValueKind == JsonValueKind.String
                        ? pt.GetString() : null;

                    // Running cumulative total — the last one wins as the session total.
                    if (pType == "token_count")
                    {
                        if (payload.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object
                            && info.TryGetProperty("total_token_usage", out var u) && u.ValueKind == JsonValueKind.Object)
                        {
                            var input = Long(u, "input_tokens");
                            var cachedIn = Long(u, "cached_input_tokens");
                            // input_tokens includes the cached portion; exclude it from the totalizer.
                            lastTokens = Math.Max(input - cachedIn, 0) + Long(u, "output_tokens");
                        }
                        continue;
                    }

                    if (pType is not ("user_message" or "agent_message")) continue;

                    messages++;
                    if (pType == "agent_message" && currentModel is { Length: > 0 } model)
                        byModel[model] = byModel.GetValueOrDefault(model) + 1;

                    if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
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

        var stats = new FileStats(fi.Length, fi.LastWriteTimeUtc, lastTokens, messages, byDay, byHour, byModel, sessionId, cwd);
        _cache[path] = stats;
        return stats;
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private sealed record FileStats(
        long Length, DateTime Mtime, long Tokens, long Messages,
        Dictionary<DateOnly, int> ByDay, int[] ByHour,
        Dictionary<string, int> ByModel, string? SessionId, string? Cwd)
    {
        public static readonly FileStats Empty =
            new(0, default, 0, 0, new(), new int[24], new(), null, null);
    }
}

using System.Text.Json;

namespace AiUsageHud.Core.TokenTracking;

/// <summary>
/// Reads Codex CLI session rollouts (<c>~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl</c>)
/// and aggregates token usage for the <em>active</em> project — the workspace
/// (<c>cwd</c>) of the most recently written rollout.
///
/// Each rollout begins with a <c>session_meta</c> line carrying the <c>cwd</c>, and
/// emits <c>token_count</c> events whose <c>info.total_token_usage</c> is the running
/// session total. The session total is therefore the <em>last</em> such event; the
/// project total sums that across rollouts sharing the same <c>cwd</c>. Parsed files
/// are cached by length+mtime so only the live rollout is re-read.
/// </summary>
public sealed class CodexTokenReader
{
    private readonly string _sessionsDir;
    private readonly Dictionary<string, FileAgg> _cache = new();

    public CodexTokenReader(string? sessionsDir = null)
    {
        _sessionsDir = sessionsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex", "sessions");
    }

    /// <summary>Aggregate the active workspace's Codex tokens, or null when unavailable.</summary>
    public ProjectTokenUsage? Read()
    {
        if (!Directory.Exists(_sessionsDir)) return null;

        string[] all;
        try { all = Directory.GetFiles(_sessionsDir, "*.jsonl", SearchOption.AllDirectories); }
        catch { return null; }
        if (all.Length == 0) return null;

        lock (_cache)
        {
            var active = MostRecentFile.Pick(all)!;
            var activeAgg = Parse(active);
            var cwd = activeAgg.Cwd;

            var session = activeAgg.Total;
            var projectTotal = session;

            // Per-model breakdown: token_count is a session-wide running total, so each
            // session's tokens are attributed to its model (from turn_context). Accurate
            // for the common one-model-per-session case.
            var byModel = new Dictionary<string, TokenTotals>();
            void Attribute(FileAgg a)
            {
                if (!string.IsNullOrEmpty(a.Model))
                    byModel[a.Model!] = byModel.GetValueOrDefault(a.Model!) + a.Total;
            }

            // Sum sessions that share the active workspace (skip the active one, already counted).
            if (!string.IsNullOrEmpty(cwd))
            {
                projectTotal = default;
                foreach (var f in all)
                {
                    var agg = Parse(f);
                    if (string.Equals(agg.Cwd, cwd, StringComparison.OrdinalIgnoreCase))
                    {
                        projectTotal += agg.Total;
                        Attribute(agg);
                    }
                }
            }
            else
            {
                Attribute(activeAgg);
            }

            var models = byModel
                .Select(kv => new ModelTokens(kv.Key, kv.Value))
                .OrderByDescending(m => m.Totals.NonCache)
                .ToList();

            var project = string.IsNullOrEmpty(cwd) ? "Codex session" : cwd!;
            return new ProjectTokenUsage(project, session, projectTotal, models);
        }
    }

    private FileAgg Parse(string path)
    {
        FileInfo fi;
        try { fi = new FileInfo(path); } catch { return FileAgg.Empty; }

        if (_cache.TryGetValue(path, out var cached)
            && cached.Length == fi.Length && cached.Mtime == fi.LastWriteTimeUtc)
            return cached;

        string? cwd = null, model = null;
        var last = default(TokenTotals);
        var sawToken = false;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length < 12) continue;

                if (cwd is null && line.Contains("\"session_meta\"", StringComparison.Ordinal))
                {
                    cwd = TryReadCwd(line);
                    continue;
                }
                if (line.Contains("\"turn_context\"", StringComparison.Ordinal))
                {
                    if (TryReadModel(line) is { Length: > 0 } m) model = m;
                    continue;
                }
                if (!line.Contains("\"token_count\"", StringComparison.Ordinal)) continue;

                if (TryReadTotals(line, out var t)) { last = t; sawToken = true; }
            }
        }
        catch { /* unreadable — treat as empty */ }

        var agg = new FileAgg(fi.Length, fi.LastWriteTimeUtc, cwd, model, sawToken ? last : default);
        _cache[path] = agg;
        return agg;
    }

    private static string? TryReadCwd(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("payload", out var p)
                && p.TryGetProperty("cwd", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() : null;
        }
        catch { return null; }
    }

    private static string? TryReadModel(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("payload", out var p)
                && p.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() : null;
        }
        catch { return null; }
    }

    private static bool TryReadTotals(string line, out TokenTotals totals)
    {
        totals = default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("payload", out var p)) return false;
            if (!p.TryGetProperty("type", out var ty) || ty.GetString() != "token_count") return false;
            if (!p.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object) return false;
            if (!info.TryGetProperty("total_token_usage", out var u) || u.ValueKind != JsonValueKind.Object) return false;

            var input = Long(u, "input_tokens");
            var cached = Long(u, "cached_input_tokens");
            var output = Long(u, "output_tokens");
            // Codex's input_tokens includes the cached portion; output includes reasoning.
            totals = new TokenTotals(Math.Max(input - cached, 0), output, 0, cached);
            return true;
        }
        catch { return false; }
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private sealed record FileAgg(long Length, DateTime Mtime, string? Cwd, string? Model, TokenTotals Total)
    {
        public static readonly FileAgg Empty = new(0, default, null, null, default);
    }
}

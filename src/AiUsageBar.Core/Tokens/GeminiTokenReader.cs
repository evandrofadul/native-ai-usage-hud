using System.Text.Json;

namespace AiUsageBar.Core.TokenTracking;

/// <summary>
/// Reads Gemini CLI's local chat transcripts
/// (<c>~/.gemini/tmp/&lt;project&gt;/chats/**/*.jsonl</c>) and aggregates token usage for
/// the <em>active</em> project — the one whose chat file was written most recently.
///
/// Each assistant turn is a top-level <c>type:"gemini"</c> line carrying a
/// <c>tokens</c> block (<c>input</c> / <c>output</c> / <c>cached</c> / <c>thoughts</c> /
/// <c>tool</c>) and a <c>model</c>. The counts are per-turn (not a running total), so
/// they sum directly — the Gemini analogue of <see cref="ClaudeTokenReader"/>. Parsed
/// files are cached by length+mtime so unchanged sessions are never re-read.
/// </summary>
public sealed class GeminiTokenReader
{
    private readonly string _tmpDir;
    private readonly Dictionary<string, FileAgg> _cache = new();

    public GeminiTokenReader(string? tmpDir = null)
    {
        _tmpDir = tmpDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gemini", "tmp");
    }

    /// <summary>Aggregate the active project's tokens, or null when nothing is available.</summary>
    public ProjectTokenUsage? Read()
    {
        if (!Directory.Exists(_tmpDir)) return null;

        string[] all;
        try { all = Directory.GetFiles(_tmpDir, "*.jsonl", SearchOption.AllDirectories); }
        catch { return null; }
        if (all.Length == 0) return null;

        // Active project = the top-level folder (under tmp) owning the most recent file.
        var latest = all.MaxBy(SafeMtime)!;
        var rel = Path.GetRelativePath(_tmpDir, latest);
        var project = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        var projectDir = Path.Combine(_tmpDir, project);
        var chatsDir = Path.Combine(projectDir, "chats");

        lock (_cache)
        {
            // Session = the newest top-level chat file (subagent transcripts live in a
            // nested <sessionId> folder and count only toward the project total).
            string[] topLevel;
            try { topLevel = Directory.GetFiles(chatsDir, "*.jsonl", SearchOption.TopDirectoryOnly); }
            catch { topLevel = []; }
            var sessionFile = topLevel.Length > 0 ? topLevel.MaxBy(SafeMtime) : latest;

            var session = sessionFile is null ? default : Parse(sessionFile).Total;

            var projectTotal = default(TokenTotals);
            var byModel = new Dictionary<string, TokenTotals>();
            string[] projectFiles;
            try { projectFiles = Directory.GetFiles(projectDir, "*.jsonl", SearchOption.AllDirectories); }
            catch { projectFiles = sessionFile is null ? [] : [sessionFile]; }
            foreach (var f in projectFiles)
            {
                var agg = Parse(f);
                projectTotal += agg.Total;
                foreach (var (model, t) in agg.ByModel)
                    byModel[model] = byModel.GetValueOrDefault(model) + t;
            }

            var models = byModel
                .Select(kv => new ModelTokens(kv.Key, kv.Value))
                .OrderByDescending(m => m.Totals.NonCache)
                .ToList();

            return new ProjectTokenUsage(project, session, projectTotal, models);
        }
    }

    private static long SafeMtime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path).Ticks; } catch { return 0; }
    }

    private FileAgg Parse(string path)
    {
        FileInfo fi;
        try { fi = new FileInfo(path); } catch { return FileAgg.Empty; }

        if (_cache.TryGetValue(path, out var cached)
            && cached.Length == fi.Length && cached.Mtime == fi.LastWriteTimeUtc)
            return cached;

        var total = default(TokenTotals);
        var byModel = new Dictionary<string, TokenTotals>();

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                // Cheap pre-filter: only assistant turns carry a tokens block.
                if (line.Length < 20 || !line.Contains("\"tokens\"", StringComparison.Ordinal)) continue;

                if (!GeminiTokens.TryRead(line, out var model, out var t)) continue;

                total += t;
                if (model.Length > 0)
                    byModel[model] = byModel.GetValueOrDefault(model) + t;
            }
        }
        catch { /* unreadable file — treat as empty */ }

        var agg = new FileAgg(fi.Length, fi.LastWriteTimeUtc, total, byModel);
        _cache[path] = agg;
        return agg;
    }

    private sealed record FileAgg(long Length, DateTime Mtime, TokenTotals Total, Dictionary<string, TokenTotals> ByModel)
    {
        public static readonly FileAgg Empty = new(0, default, default, new());
    }
}

/// <summary>
/// Shared parsing for a Gemini chat line's <c>tokens</c> block. A turn is a top-level
/// <c>type:"gemini"</c> object; <c>input</c> already includes the <c>cached</c> portion
/// (so the non-cache input is <c>input − cached</c>), and <c>thoughts</c>/<c>tool</c>
/// are billed reasoning/tool tokens folded into output.
/// </summary>
public static class GeminiTokens
{
    public static bool TryRead(string line, out string model, out TokenTotals totals)
    {
        model = "";
        totals = default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("type", out var ty) || ty.GetString() != "gemini") return false;
            if (!root.TryGetProperty("tokens", out var tk) || tk.ValueKind != JsonValueKind.Object) return false;

            var input = Long(tk, "input");
            var output = Long(tk, "output");
            var cached = Long(tk, "cached");
            var thoughts = Long(tk, "thoughts");
            var tool = Long(tk, "tool");

            if (root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
                model = m.GetString() ?? "";

            totals = new TokenTotals(Math.Max(input - cached, 0), output + thoughts + tool, 0, cached);
            return true;
        }
        catch { return false; }
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}

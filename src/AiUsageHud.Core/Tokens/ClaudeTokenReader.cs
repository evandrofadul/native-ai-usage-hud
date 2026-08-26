using System.Text.Json;

namespace AiUsageHud.Core.TokenTracking;

/// <summary>
/// Reads Claude Code's local session transcripts (<c>~/.claude/projects/&lt;project&gt;/*.jsonl</c>)
/// and aggregates token usage for the <em>active</em> project — the one whose
/// session file was written most recently.
///
/// Each assistant line carries a <c>message.usage</c> block
/// (<c>input_tokens</c> / <c>output_tokens</c> / <c>cache_creation_input_tokens</c> /
/// <c>cache_read_input_tokens</c>) and a <c>message.model</c>. Synthetic messages
/// (model <c>&lt;synthetic&gt;</c>) are skipped. Parsed files are cached by
/// length+mtime so unchanged sessions are never re-read.
/// </summary>
public sealed class ClaudeTokenReader
{
    private readonly string _projectsDir;
    private readonly Dictionary<string, FileAgg> _cache = new();

    public ClaudeTokenReader(string? projectsDir = null)
    {
        _projectsDir = projectsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
    }

    /// <summary>Aggregate the active project's tokens, or null when nothing is available.</summary>
    public ProjectTokenUsage? Read()
    {
        if (!Directory.Exists(_projectsDir)) return null;

        string[] all;
        try { all = Directory.GetFiles(_projectsDir, "*.jsonl", SearchOption.AllDirectories); }
        catch { return null; }
        if (all.Length == 0) return null;

        // Active project = the top-level folder owning the most recently touched file.
        var latest = MostRecentFile.Pick(all)!;
        var rel = Path.GetRelativePath(_projectsDir, latest);
        var project = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        var projectDir = Path.Combine(_projectsDir, project);

        lock (_cache)
        {
            // Session = the newest session file directly in the project folder
            // (subagent transcripts live in a nested folder and count only toward the project).
            string[] topLevel;
            try { topLevel = Directory.GetFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly); }
            catch { topLevel = []; }
            var sessionFile = topLevel.Length > 0 ? MostRecentFile.Pick(topLevel) : latest;

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
                // Cheap pre-filter: only assistant lines with a usage block matter.
                if (line.Length < 20 || !line.Contains("\"usage\"", StringComparison.Ordinal)) continue;

                TokenTotals t;
                string model;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("message", out var msg)) continue;
                    if (!msg.TryGetProperty("usage", out var usage)) continue;

                    model = msg.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString() ?? "" : "";
                    if (model is "" or "<synthetic>") continue;

                    t = new TokenTotals(
                        Long(usage, "input_tokens"),
                        Long(usage, "output_tokens"),
                        Long(usage, "cache_creation_input_tokens"),
                        Long(usage, "cache_read_input_tokens"));
                }
                catch { continue; }

                total += t;
                byModel[model] = byModel.GetValueOrDefault(model) + t;
            }
        }
        catch { /* unreadable file — treat as empty */ }

        var agg = new FileAgg(fi.Length, fi.LastWriteTimeUtc, total, byModel);
        _cache[path] = agg;
        return agg;
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private sealed record FileAgg(long Length, DateTime Mtime, TokenTotals Total, Dictionary<string, TokenTotals> ByModel)
    {
        public static readonly FileAgg Empty = new(0, default, default, new());
    }
}

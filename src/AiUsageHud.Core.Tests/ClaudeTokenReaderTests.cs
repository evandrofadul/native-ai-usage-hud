using AiUsageHud.Core.TokenTracking;

namespace AiUsageHud.Core.Tests;

public class ClaudeTokenReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-ub-tok-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private string WriteSession(string project, string name, params string[] lines)
    {
        var dir = Path.Combine(_root, project);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
        return path;
    }

    private static string Assistant(string model, long input, long output, long cacheCreate, long cacheRead) =>
        "{\"type\":\"assistant\",\"timestamp\":\"2026-05-29T12:00:00Z\",\"message\":{\"role\":\"assistant\",\"model\":\""
        + model + "\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output
        + ",\"cache_creation_input_tokens\":" + cacheCreate + ",\"cache_read_input_tokens\":" + cacheRead + "}}}";

    [Fact]
    public void AggregatesSessionAndProjectByModel()
    {
        WriteSession("proj-a", "s1.jsonl",
            """{"type":"user","message":{"role":"user","content":"hi"}}""",
            Assistant("claude-opus-4-8", 100, 200, 300, 400),
            Assistant("claude-opus-4-8", 1, 2, 3, 4));

        var reader = new ClaudeTokenReader(_root);
        var usage = reader.Read();

        Assert.NotNull(usage);
        Assert.Equal("proj-a", usage!.Project);
        // Session = the single (newest) session file: 101/202/303/404.
        Assert.Equal(101, usage.Session.Input);
        Assert.Equal(202, usage.Session.Output);
        Assert.Equal(303 + 404, usage.Session.Cache);
        Assert.Equal(1010, usage.Session.Total);

        var model = Assert.Single(usage.ByModel);
        Assert.Equal("claude-opus-4-8", model.Model);
        Assert.Equal(1010, model.Totals.Total);
    }

    [Fact]
    public void SkipsSyntheticMessages()
    {
        WriteSession("proj-b", "s1.jsonl",
            Assistant("<synthetic>", 999, 999, 999, 999),
            Assistant("claude-sonnet-4-6", 10, 20, 0, 0));

        var usage = new ClaudeTokenReader(_root).Read();

        Assert.NotNull(usage);
        Assert.Equal(30, usage!.Session.Total);
        Assert.Equal("claude-sonnet-4-6", Assert.Single(usage.ByModel).Model);
    }

    [Fact]
    public void PicksMostRecentlyWrittenProjectAsActive()
    {
        var older = WriteSession("proj-old", "s.jsonl", Assistant("claude-opus-4-8", 5, 5, 0, 0));
        var newer = WriteSession("proj-new", "s.jsonl", Assistant("claude-opus-4-8", 7, 7, 0, 0));
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var usage = new ClaudeTokenReader(_root).Read();

        Assert.Equal("proj-new", usage!.Project);
        Assert.Equal(14, usage.Session.Total);
    }

    [Fact]
    public void ReturnsNullWhenNoProjectsDir()
    {
        var usage = new ClaudeTokenReader(Path.Combine(_root, "does-not-exist")).Read();
        Assert.Null(usage);
    }
}

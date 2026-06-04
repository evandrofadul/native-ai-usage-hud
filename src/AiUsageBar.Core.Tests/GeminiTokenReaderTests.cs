using AiUsageBar.Core.TokenTracking;

namespace AiUsageBar.Core.Tests;

public class GeminiTokenReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-ub-gem-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>Write a chat file under <c>&lt;project&gt;/chats/&lt;name&gt;</c> (the Gemini layout).</summary>
    private string WriteSession(string project, string name, params string[] lines)
    {
        var dir = Path.Combine(_root, project, "chats");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
        return path;
    }

    private static string Meta(string sessionId) =>
        "{\"sessionId\":\"" + sessionId + "\",\"projectHash\":\"h\",\"startTime\":\"2026-06-02T12:00:00Z\",\"kind\":\"main\"}";

    private static string Turn(string model, long input, long output, long cached, long thoughts, long tool,
        string ts = "2026-06-02T12:00:00Z") =>
        "{\"type\":\"gemini\",\"timestamp\":\"" + ts + "\",\"model\":\"" + model
        + "\",\"tokens\":{\"input\":" + input + ",\"output\":" + output + ",\"cached\":" + cached
        + ",\"thoughts\":" + thoughts + ",\"tool\":" + tool + ",\"total\":0}}";

    [Fact]
    public void AggregatesSessionAndProjectByModel()
    {
        WriteSession("proj-a", "s1.jsonl",
            Meta("sess-1"),
            """{"type":"user","timestamp":"2026-06-02T12:00:00Z","content":[{"text":"hi"}]}""",
            // input includes the cached portion; output folds in thoughts + tool.
            Turn("gemini-2.5-flash", 100, 200, 10, 5, 0),
            Turn("gemini-2.5-flash", 50, 10, 0, 0, 0));

        var usage = new GeminiTokenReader(_root).Read();

        Assert.NotNull(usage);
        Assert.Equal("proj-a", usage!.Project);
        // Input = (100-10) + (50-0) = 140; Output = (200+5) + 10 = 215; CacheRead = 10.
        Assert.Equal(140, usage.Session.Input);
        Assert.Equal(215, usage.Session.Output);
        Assert.Equal(10, usage.Session.Cache);
        Assert.Equal(355, usage.Session.NonCache);

        var model = Assert.Single(usage.ByModel);
        Assert.Equal("gemini-2.5-flash", model.Model);
        Assert.Equal(365, model.Totals.Total);
    }

    [Fact]
    public void IgnoresLinesWithoutTopLevelGeminiTokens()
    {
        WriteSession("proj-b", "s1.jsonl",
            Meta("sess-b"),
            // A checkpoint line that nests a "tokens" string but is not a gemini turn → ignored.
            """{"messages":[{"type":"user","content":"see tokens"}],"lastUpdated":"x"}""",
            """{"type":"thought","timestamp":"2026-06-02T12:00:00Z","content":"thinking"}""",
            Turn("gemini-3-flash-preview", 10, 20, 0, 0, 0));

        var usage = new GeminiTokenReader(_root).Read();

        Assert.NotNull(usage);
        Assert.Equal(30, usage!.Session.NonCache);
        Assert.Equal("gemini-3-flash-preview", Assert.Single(usage.ByModel).Model);
    }

    [Fact]
    public void PicksMostRecentlyWrittenProjectAsActive()
    {
        var older = WriteSession("proj-old", "s.jsonl", Turn("gemini-2.5-flash", 5, 5, 0, 0, 0));
        var newer = WriteSession("proj-new", "s.jsonl", Turn("gemini-2.5-flash", 7, 7, 0, 0, 0));
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var usage = new GeminiTokenReader(_root).Read();

        Assert.Equal("proj-new", usage!.Project);
        Assert.Equal(14, usage.Session.NonCache);
    }

    [Fact]
    public void ReturnsNullWhenNoTmpDir()
    {
        Assert.Null(new GeminiTokenReader(Path.Combine(_root, "nope")).Read());
    }

    [Fact]
    public void StatsAggregateSessionsMessagesAndModel()
    {
        WriteSession("proj-a", "s1.jsonl",
            Meta("sess-1"),
            """{"type":"user","timestamp":"2026-06-02T12:00:00Z","content":[{"text":"hi"}]}""",
            Turn("gemini-2.5-flash", 100, 200, 10, 5, 0));
        WriteSession("proj-a", "s2.jsonl",
            Meta("sess-2"),
            Turn("gemini-2.5-flash", 50, 10, 0, 0, 0));

        var stats = new GeminiUsageStatsReader(_root).Read();

        Assert.NotNull(stats);
        Assert.Equal("proj-a", stats!.Project);
        Assert.Equal(2, stats.Sessions);          // two distinct sessionIds
        Assert.Equal(3, stats.Messages);          // 1 user + 2 gemini turns
        // Totalizer excludes cache: (100-10)+200+5 + 50+10 = 295 + 60 = 355.
        Assert.Equal(355, stats.TotalTokens);
        Assert.Equal("gemini-2.5-flash", stats.FavoriteModel);
    }
}

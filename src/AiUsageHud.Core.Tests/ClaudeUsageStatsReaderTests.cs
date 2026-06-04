using AiUsageHud.Core.TokenTracking;

namespace AiUsageHud.Core.Tests;

public class ClaudeUsageStatsReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-ub-stats-" + Guid.NewGuid().ToString("N"));

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

    /// <summary>An assistant line with a UTC timestamp, model and usage.</summary>
    private static string Assistant(string sessionId, string isoUtc, string model, long input, long output) =>
        "{\"type\":\"assistant\",\"sessionId\":\"" + sessionId + "\",\"timestamp\":\"" + isoUtc
        + "\",\"message\":{\"role\":\"assistant\",\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":"
        + input + ",\"output_tokens\":" + output + ",\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";

    private static string User(string sessionId, string isoUtc) =>
        "{\"type\":\"user\",\"sessionId\":\"" + sessionId + "\",\"timestamp\":\"" + isoUtc
        + "\",\"message\":{\"role\":\"user\",\"content\":\"hi\"}}";

    [Fact]
    public void AggregatesCountsTokensModelsAndDays()
    {
        // Two sessions in the active project, three active days, two models.
        WriteSession("proj-a", "s1.jsonl",
            User("s1", "2026-05-20T10:00:00Z"),
            Assistant("s1", "2026-05-20T10:00:01Z", "claude-opus-4-8", 100, 50),
            Assistant("s1", "2026-05-21T10:00:00Z", "claude-opus-4-8", 10, 5));
        WriteSession("proj-a", "s2.jsonl",
            User("s2", "2026-05-22T09:00:00Z"),
            Assistant("s2", "2026-05-22T09:00:01Z", "claude-sonnet-4-6", 1, 1));

        var stats = new ClaudeUsageStatsReader(_root).Read();

        Assert.NotNull(stats);
        Assert.Equal("proj-a", stats!.Project);
        Assert.Equal(2, stats.Sessions);                 // s1 + s2
        Assert.Equal(5, stats.Messages);                 // 2 user + 3 assistant
        Assert.Equal(100 + 50 + 10 + 5 + 1 + 1, stats.TotalTokens);
        Assert.Equal(3, stats.ActiveDays);               // 20th, 21st, 22nd
        Assert.Equal("Opus 4.8", stats.FavoriteModel);   // opus used twice, sonnet once
        Assert.Equal(ClaudeUsageStatsReader.HeatmapDays, stats.Heatmap.Count);
    }

    [Fact]
    public void ComputesLongestStreak()
    {
        // Active days 10,11,12 (run of 3), then a gap, then 15.
        WriteSession("proj-b", "s.jsonl",
            Assistant("b", "2026-05-10T08:00:00Z", "claude-opus-4-8", 1, 1),
            Assistant("b", "2026-05-11T08:00:00Z", "claude-opus-4-8", 1, 1),
            Assistant("b", "2026-05-12T08:00:00Z", "claude-opus-4-8", 1, 1),
            Assistant("b", "2026-05-15T08:00:00Z", "claude-opus-4-8", 1, 1));

        var stats = new ClaudeUsageStatsReader(_root).Read();

        Assert.Equal(4, stats!.ActiveDays);
        Assert.Equal(3, stats.LongestStreak);
    }

    [Fact]
    public void SkipsSyntheticModelForFavorite()
    {
        WriteSession("proj-c", "s.jsonl",
            Assistant("c", "2026-05-10T08:00:00Z", "<synthetic>", 1, 1),
            Assistant("c", "2026-05-10T09:00:00Z", "claude-sonnet-4-6", 1, 1));

        var stats = new ClaudeUsageStatsReader(_root).Read();

        Assert.Equal("Sonnet 4.6", stats!.FavoriteModel);
    }

    [Fact]
    public void ReturnsNullWhenNoProjectsDir()
    {
        Assert.Null(new ClaudeUsageStatsReader(Path.Combine(_root, "nope")).Read());
    }
}

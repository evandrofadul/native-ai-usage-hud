using AiUsageHud.Core.TokenTracking;

namespace AiUsageHud.Core.Tests;

public class AntigravityUsageStatsReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-ub-agystats-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>Writes a transcript.jsonl under the expected brain directory structure.</summary>
    private void WriteTranscript(string conversationId, params string[] lines)
    {
        var dir = Path.Combine(_root, conversationId, ".system_generated", "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "transcript.jsonl");
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
    }

    /// <summary>Produces a USER_INPUT JSONL line, optionally including a model-change settings block.</summary>
    private static string UserInput(string isoUtc, string? modelChange = null)
    {
        var content = "hi";
        if (modelChange is not null)
            content = $"<USER_REQUEST>\\nhi\\n</USER_REQUEST>\\n<USER_SETTINGS_CHANGE>\\nThe user changed setting `Model Selection` from None to {modelChange}. No need to comment.\\n</USER_SETTINGS_CHANGE>";
        return "{\"step_index\":0,\"source\":\"USER_EXPLICIT\",\"type\":\"USER_INPUT\",\"status\":\"DONE\",\"created_at\":\"" + isoUtc + "\",\"content\":\"" + content + "\"}";
    }

    /// <summary>Produces a PLANNER_RESPONSE JSONL line.</summary>
    private static string PlannerResponse(string isoUtc) =>
        "{\"step_index\":1,\"source\":\"MODEL\",\"type\":\"PLANNER_RESPONSE\",\"status\":\"DONE\",\"created_at\":\"" + isoUtc + "\",\"content\":\"response text\"}";

    [Fact]
    public void AggregatesCountsAndDays()
    {
        // Two conversations, three active days.
        WriteTranscript("conv-a",
            UserInput("2026-05-20T10:00:00Z"),
            PlannerResponse("2026-05-20T10:00:01Z"),
            PlannerResponse("2026-05-21T10:00:00Z"));
        WriteTranscript("conv-b",
            UserInput("2026-05-22T09:00:00Z"),
            PlannerResponse("2026-05-22T09:00:01Z"));

        var stats = new AntigravityUsageStatsReader(_root).Read();

        Assert.NotNull(stats);
        Assert.Equal(2, stats!.Sessions);                                  // conv-a + conv-b
        Assert.Equal(5, stats.Messages);                                   // 2 user + 3 planner
        Assert.Equal(0, stats.TotalTokens);                                // tokens not available
        Assert.Equal(3, stats.ActiveDays);                                 // 20th, 21st, 22nd
        Assert.Equal(AntigravityUsageStatsReader.HeatmapDays, stats.Heatmap.Count);
    }

    [Fact]
    public void ComputesLongestStreak()
    {
        // Active days 10, 11, 12 (run of 3), then a gap, then 15.
        WriteTranscript("conv-streak",
            PlannerResponse("2026-05-10T08:00:00Z"),
            PlannerResponse("2026-05-11T08:00:00Z"),
            PlannerResponse("2026-05-12T08:00:00Z"),
            PlannerResponse("2026-05-15T08:00:00Z"));

        var stats = new AntigravityUsageStatsReader(_root).Read();

        Assert.Equal(4, stats!.ActiveDays);
        Assert.Equal(3, stats.LongestStreak);
    }

    [Fact]
    public void ExtractsModelFromSettingsChange()
    {
        WriteTranscript("conv-model",
            UserInput("2026-05-20T10:00:00Z", "claude-sonnet-4"),
            PlannerResponse("2026-05-20T10:00:01Z"));

        var stats = new AntigravityUsageStatsReader(_root).Read();

        Assert.Equal("claude-sonnet-4", stats!.FavoriteModel);
    }

    [Fact]
    public void ReturnsNullWhenNoBrainDir()
    {
        Assert.Null(new AntigravityUsageStatsReader(Path.Combine(_root, "nope")).Read());
    }
}

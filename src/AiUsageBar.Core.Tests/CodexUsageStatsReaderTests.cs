using AiUsageBar.Core.TokenTracking;
using Xunit;

namespace AiUsageBar.Core.Tests;

public class CodexUsageStatsReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-ub-cxstats-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>Writes a rollout under YYYY/MM/DD like Codex does, returning its path.</summary>
    private string WriteRollout(string day, string name, params string[] lines)
    {
        var dir = Path.Combine(_root, day.Replace('-', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
        return path;
    }

    private static string SessionMeta(string id, string cwd) =>
        "{\"timestamp\":\"2026-05-20T10:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\""
        + id + "\",\"cwd\":\"" + cwd + "\"}}";

    private static string TurnContext(string model) =>
        "{\"timestamp\":\"2026-05-20T10:00:00.100Z\",\"type\":\"turn_context\",\"payload\":{\"model\":\"" + model + "\"}}";

    private static string UserMsg(string isoUtc) =>
        "{\"timestamp\":\"" + isoUtc + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\"}}";

    private static string AgentMsg(string isoUtc) =>
        "{\"timestamp\":\"" + isoUtc + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_message\"}}";

    /// <summary>A token_count event: the cumulative running total for the session so far.</summary>
    private static string TokenCount(long input, long cached, long output) =>
        "{\"timestamp\":\"2026-05-20T10:00:02.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{"
        + "\"total_token_usage\":{\"input_tokens\":" + input + ",\"cached_input_tokens\":" + cached
        + ",\"output_tokens\":" + output + "}}}}";

    [Fact]
    public void AggregatesSessionsMessagesTokensModelsAndDays()
    {
        WriteRollout("2026-05-20", "rollout-a.jsonl",
            SessionMeta("sess-a", "C:/work/proj-a"),
            TurnContext("gpt-5.5"),
            UserMsg("2026-05-20T10:00:00Z"),
            AgentMsg("2026-05-20T10:00:01Z"),
            TokenCount(100, 30, 50),          // earlier cumulative snapshot
            AgentMsg("2026-05-21T10:00:00Z"),
            TokenCount(300, 80, 120));        // last one wins: (300-80)+120 = 340
        WriteRollout("2026-05-22", "rollout-b.jsonl",
            SessionMeta("sess-b", "C:/work/proj-a"),
            TurnContext("gpt-5.5"),
            UserMsg("2026-05-22T09:00:00Z"),
            AgentMsg("2026-05-22T09:00:01Z"),
            TokenCount(10, 0, 5));            // (10-0)+5 = 15

        var stats = new CodexUsageStatsReader(_root).Read();

        Assert.NotNull(stats);
        Assert.Equal(2, stats!.Sessions);                 // sess-a + sess-b
        Assert.Equal(5, stats.Messages);                  // 2 user + 3 agent
        Assert.Equal(340 + 15, stats.TotalTokens);        // last token_count per file, cache excluded
        Assert.Equal(3, stats.ActiveDays);                // 20th, 21st, 22nd
        Assert.Equal("gpt-5.5", stats.FavoriteModel);     // model carried by turn_context
        Assert.Equal(CodexUsageStatsReader.HeatmapDays, stats.Heatmap.Count);
    }

    [Fact]
    public void ReturnsNullWhenNoSessionsDir()
    {
        Assert.Null(new CodexUsageStatsReader(Path.Combine(_root, "nope")).Read());
    }
}

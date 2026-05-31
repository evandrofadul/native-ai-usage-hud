using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Json;
using AiUsageBar.Core.Models;

namespace AiUsageBar.Core.Vendors.Zai;

/// <summary>
/// Wire types for the undocumented Z.AI monitor endpoint
/// <c>/api/monitor/usage/quota/limit</c>. Limits are classified by position+type:
/// first TOKENS_LIMIT = session, second = weekly, TIME_LIMIT = monthly MCP.
/// Port of the Rust <c>zai/types.rs</c>.
/// </summary>
public sealed class ZaiEnvelope
{
    [JsonPropertyName("code")] public long Code { get; set; }
    [JsonPropertyName("data")] public MonitorData? Data { get; set; }
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("msg")] public string Msg { get; set; } = "";

    public static ZaiEnvelope Parse(byte[] bytes) =>
        JsonSerializer.Deserialize<ZaiEnvelope>(bytes, JsonDefaults.Options) ?? new();

    public ZaiSnapshot ToSnapshot(string? configPlanTier)
    {
        var data = Data ?? new MonitorData();
        var tokens = data.Limits.Where(l => l.Kind == "TOKENS_LIMIT").ToList();
        UsageWindow? session = tokens.Count > 0 ? ToWindow(tokens[0], TimeSpan.FromHours(5)) : null;
        UsageWindow? weekly = tokens.Count > 1 ? ToWindow(tokens[1], TimeSpan.FromDays(7)) : null;
        var timeLimit = data.Limits.FirstOrDefault(l => l.Kind == "TIME_LIMIT");
        UsageWindow? mcp = timeLimit is not null ? ToWindow(timeLimit, TimeSpan.FromDays(30)) : null;

        var level = !string.IsNullOrEmpty(data.Level) ? data.Level : configPlanTier ?? "unknown";
        var plan = $"GLM Coding {Capitalize(level)}";
        return new ZaiSnapshot(plan, session, weekly, mcp);
    }

    private static UsageWindow ToWindow(LimitEntry l, TimeSpan dur)
    {
        var pct = (int)Math.Clamp(Math.Round(l.Percentage), 0, 100);
        DateTimeOffset? reset = l.NextResetTime is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
        return new UsageWindow(pct, reset, dur);
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

public sealed class MonitorData
{
    [JsonPropertyName("limits")] public List<LimitEntry> Limits { get; set; } = [];
    [JsonPropertyName("level")] public string Level { get; set; } = "";
}

public sealed class LimitEntry
{
    [JsonPropertyName("type")] public string Kind { get; set; } = "";
    [JsonPropertyName("percentage")] public double Percentage { get; set; }

    [JsonPropertyName("nextResetTime")]
    [JsonConverter(typeof(LenientNullableLongConverter))]
    public long? NextResetTime { get; set; }
}

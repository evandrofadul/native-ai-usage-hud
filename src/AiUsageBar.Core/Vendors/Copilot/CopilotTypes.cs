using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Json;
using AiUsageBar.Core.Models;

namespace AiUsageBar.Core.Vendors.Copilot;

/// <summary>
/// Wire types for <c>GET https://api.github.com/copilot_internal/user</c>. The response
/// carries the account's plan and the monthly quota buckets (premium requests / chat /
/// completions) — the usage signal we surface. Unknown members are ignored.
/// </summary>
public sealed class CopilotUserResponse
{
    [JsonPropertyName("copilot_plan")] public string? CopilotPlan { get; set; }
    [JsonPropertyName("chat_enabled")] public bool ChatEnabled { get; set; }
    [JsonPropertyName("quota_reset_date")] public string? QuotaResetDate { get; set; }
    [JsonPropertyName("quota_snapshots")] public Dictionary<string, CopilotQuotaSnapshot>? QuotaSnapshots { get; set; }

    public static CopilotUserResponse Parse(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, AppJsonContext.Default.CopilotUserResponse) ?? new();

    public CopilotSnapshot ToSnapshot()
    {
        var plan = "Copilot " + Capitalize(CopilotPlan ?? "Unknown");
        var resetsAt = ParseResetDate(QuotaResetDate);

        var quotas = new List<CopilotQuota>();
        // Surface the well-known buckets in a sensible order; append any extras after.
        foreach (var key in QuotaOrder)
            if (QuotaSnapshots is not null && QuotaSnapshots.TryGetValue(key, out var q))
                quotas.Add(ToQuota(DisplayName(key), q));

        if (QuotaSnapshots is not null)
            foreach (var (key, q) in QuotaSnapshots)
                if (!QuotaOrder.Contains(key))
                    quotas.Add(ToQuota(DisplayName(key), q));

        return new CopilotSnapshot(plan, ChatEnabled, resetsAt, quotas);
    }

    private static readonly string[] QuotaOrder =
        ["premium_interactions", "chat", "completions"];

    private static CopilotQuota ToQuota(string name, CopilotQuotaSnapshot q)
    {
        // The API reports "percent_remaining"; we display utilization (consumed).
        var used = (int)Math.Clamp(Math.Round(100.0 - q.PercentRemaining), 0, 100);
        return new CopilotQuota(name, q.Unlimited ? 0 : used, q.Unlimited, q.Remaining, q.Entitlement, q.OverageCount);
    }

    private static string DisplayName(string key) => key switch
    {
        "premium_interactions" => "Premium requests",
        "chat" => "Chat",
        "completions" => "Completions",
        _ => Capitalize(key.Replace('_', ' ')),
    };

    private static DateTimeOffset? ParseResetDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        if (DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            return dto;
        return null;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>One quota bucket from <c>quota_snapshots</c>.</summary>
public sealed class CopilotQuotaSnapshot
{
    [JsonPropertyName("entitlement")] public double Entitlement { get; set; }
    [JsonPropertyName("remaining")] public double Remaining { get; set; }
    [JsonPropertyName("percent_remaining")] public double PercentRemaining { get; set; }
    [JsonPropertyName("unlimited")] public bool Unlimited { get; set; }

    [JsonPropertyName("overage_count")]
    [JsonConverter(typeof(LenientLongConverter))]
    public long OverageCount { get; set; }
}

/// <summary>
/// Cache representation for Copilot. The session token in the live response is
/// short-lived and sensitive, so we persist only the derived snapshot (never the raw
/// exchange body) — same approach as OpenRouter.
/// </summary>
public sealed class CopilotCacheDto
{
    public string Plan { get; set; } = "Copilot";
    public bool ChatEnabled { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }
    public List<CopilotQuota> Quotas { get; set; } = [];

    public static byte[] Serialize(CopilotSnapshot s) =>
        JsonSerializer.SerializeToUtf8Bytes(new CopilotCacheDto
        {
            Plan = s.Plan,
            ChatEnabled = s.ChatEnabled,
            ResetsAt = s.ResetsAt,
            Quotas = s.Quotas.ToList(),
        }, AppJsonContext.Default.CopilotCacheDto);

    public static CopilotSnapshot Deserialize(byte[] bytes)
    {
        var d = JsonSerializer.Deserialize(bytes, AppJsonContext.Default.CopilotCacheDto) ?? new();
        return new CopilotSnapshot(d.Plan, d.ChatEnabled, d.ResetsAt, d.Quotas);
    }
}

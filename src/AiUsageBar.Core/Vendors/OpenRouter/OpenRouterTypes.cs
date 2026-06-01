using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Json;
using AiUsageBar.Core.Models;

namespace AiUsageBar.Core.Vendors.OpenRouter;

/// <summary>Wrapper used by all OpenRouter v1 endpoints (<c>{ "data": { ... } }</c>).</summary>
public sealed class OrEnvelope<T>
{
    [JsonPropertyName("data")] public T? Data { get; set; }
}

/// <summary><c>GET /api/v1/credits</c>.</summary>
public sealed class CreditsData
{
    [JsonPropertyName("total_credits")] public double TotalCredits { get; set; }
    [JsonPropertyName("total_usage")] public double TotalUsage { get; set; }
}

/// <summary><c>GET /api/v1/key</c>.</summary>
public sealed class KeyData
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("limit")] public double? Limit { get; set; }
    [JsonPropertyName("limit_remaining")] public double? LimitRemaining { get; set; }
    [JsonPropertyName("usage")] public double Usage { get; set; }
    [JsonPropertyName("usage_daily")] public double UsageDaily { get; set; }
    [JsonPropertyName("usage_weekly")] public double UsageWeekly { get; set; }
    [JsonPropertyName("usage_monthly")] public double UsageMonthly { get; set; }
    [JsonPropertyName("is_free_tier")] public bool IsFreeTier { get; set; }
}

public static class OpenRouterMapping
{
    public static OpenRouterSnapshot Combine(CreditsData credits, KeyData key)
    {
        var label = string.IsNullOrEmpty(key.Label) ? "OpenRouter" : $"OpenRouter — {key.Label}";
        return new OpenRouterSnapshot(
            label,
            credits.TotalCredits,
            credits.TotalUsage,
            key.UsageDaily,
            key.UsageWeekly,
            key.UsageMonthly,
            key.IsFreeTier,
            key.Limit,
            key.LimitRemaining);
    }

    public static CreditsData ParseCredits(string json) =>
        JsonSerializer.Deserialize(json, AppJsonContext.Default.OrEnvelopeCreditsData)?.Data ?? new();

    public static KeyData ParseKey(string json) =>
        JsonSerializer.Deserialize(json, AppJsonContext.Default.OrEnvelopeKeyData)?.Data ?? new();
}

/// <summary>
/// Cache representation for OpenRouter. The combined snapshot has no single wire
/// shape, so we serialize our own DTO into the cache file.
/// </summary>
public sealed class OpenRouterCacheDto
{
    public string Label { get; set; } = "OpenRouter";
    public double TotalCredits { get; set; }
    public double TotalUsage { get; set; }
    public double UsageDaily { get; set; }
    public double UsageWeekly { get; set; }
    public double UsageMonthly { get; set; }
    public bool IsFreeTier { get; set; }
    public double? Limit { get; set; }
    public double? LimitRemaining { get; set; }

    public static byte[] Serialize(OpenRouterSnapshot s) =>
        JsonSerializer.SerializeToUtf8Bytes(new OpenRouterCacheDto
        {
            Label = s.Label,
            TotalCredits = s.TotalCredits,
            TotalUsage = s.TotalUsage,
            UsageDaily = s.UsageDaily,
            UsageWeekly = s.UsageWeekly,
            UsageMonthly = s.UsageMonthly,
            IsFreeTier = s.IsFreeTier,
            Limit = s.Limit,
            LimitRemaining = s.LimitRemaining,
        }, AppJsonContext.Default.OpenRouterCacheDto);

    public static OpenRouterSnapshot Deserialize(byte[] bytes)
    {
        var d = JsonSerializer.Deserialize(bytes, AppJsonContext.Default.OpenRouterCacheDto) ?? new();
        return new OpenRouterSnapshot(d.Label, d.TotalCredits, d.TotalUsage, d.UsageDaily,
            d.UsageWeekly, d.UsageMonthly, d.IsFreeTier, d.Limit, d.LimitRemaining);
    }
}

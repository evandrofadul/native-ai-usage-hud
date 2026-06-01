using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Json;
using AiUsageBar.Core.Models;

namespace AiUsageBar.Core.Vendors.Anthropic;

/// <summary>
/// Wire types for <c>GET /api/oauth/usage</c>. Every field is optional/defaulted —
/// the endpoint is undocumented and varies across plan tiers. Port of the Rust
/// <c>anthropic/types.rs</c>.
/// </summary>
public sealed class AnthropicUsageResponse
{
    [JsonPropertyName("five_hour")] public AnthropicWindow? FiveHour { get; set; }
    [JsonPropertyName("seven_day")] public AnthropicWindow? SevenDay { get; set; }
    [JsonPropertyName("seven_day_sonnet")] public AnthropicWindow? SevenDaySonnet { get; set; }
    [JsonPropertyName("extra_usage")] public ExtraUsageBlock? ExtraUsage { get; set; }

    public static AnthropicUsageResponse Parse(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, AppJsonContext.Default.AnthropicUsageResponse) ?? new();

    public AnthropicSnapshot ToSnapshot(string planLabel)
    {
        var session = ToWindow(FiveHour, TimeSpan.FromHours(5));
        var weekly = ToWindow(SevenDay, TimeSpan.FromDays(7));
        UsageWindow? sonnet = SevenDaySonnet is null ? null : ToWindow(SevenDaySonnet, TimeSpan.FromDays(7));
        ExtraUsage? extra = ExtraUsage is { IsEnabled: true } e
            ? new ExtraUsage(new Cents(e.MonthlyLimit), new Cents(e.UsedCredits))
            : null;
        return new AnthropicSnapshot(planLabel, session, weekly, sonnet, extra);
    }

    private static UsageWindow ToWindow(AnthropicWindow? w, TimeSpan dur)
    {
        if (w is null) return new UsageWindow(0, null, dur);
        DateTimeOffset? reset = null;
        if (!string.IsNullOrEmpty(w.ResetsAt) &&
            DateTimeOffset.TryParse(w.ResetsAt, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var dt))
            reset = dt;
        return new UsageWindow((int)Math.Round(w.Utilization), reset, dur);
    }
}

public sealed class AnthropicWindow
{
    [JsonPropertyName("utilization")] public double Utilization { get; set; }
    [JsonPropertyName("resets_at")] public string? ResetsAt { get; set; }
}

public sealed class ExtraUsageBlock
{
    [JsonPropertyName("is_enabled")] public bool IsEnabled { get; set; }

    [JsonPropertyName("monthly_limit")]
    [JsonConverter(typeof(LenientLongConverter))]
    public long MonthlyLimit { get; set; }

    [JsonPropertyName("used_credits")]
    [JsonConverter(typeof(LenientLongConverter))]
    public long UsedCredits { get; set; }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageHud.Core.Json;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Vendors.Anthropic;

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
        // `used_credits` is the essential datum: without it there is nothing
        // truthful to display. `monthly_limit: null` is semantic, not drift —
        // the endpoint sends it for plans with no spending cap (e.g. Claude
        // Pro) — so it stays a separate, non-fatal absence rather than
        // discarding real credit spend along with it.
        ExtraUsage? extra = ExtraUsage is { IsEnabled: true, UsedCredits: { } spent } e
            ? new ExtraUsage(
                e.MonthlyLimit is { } limit ? new Cents(limit) : null,
                new Cents(spent),
                e.Currency,
                e.DecimalPlaces)
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
        return new UsageWindow((int)Math.Clamp(Math.Round(w.Utilization), 0, 100), reset, dur);
    }
}

public sealed class AnthropicWindow
{
    [JsonPropertyName("utilization")]
    [JsonConverter(typeof(StrictPercentConverter))]
    public double Utilization { get; set; }

    [JsonPropertyName("resets_at")] public string? ResetsAt { get; set; }
}

public sealed class ExtraUsageBlock
{
    [JsonPropertyName("is_enabled")] public bool IsEnabled { get; set; }

    /// <summary>Null for plans with no spending cap — semantic, not drift.</summary>
    [JsonPropertyName("monthly_limit")]
    [JsonConverter(typeof(LenientNullableLongConverter))]
    public long? MonthlyLimit { get; set; }

    [JsonPropertyName("used_credits")]
    [JsonConverter(typeof(LenientNullableLongConverter))]
    public long? UsedCredits { get; set; }

    /// <summary>ISO currency code ("BRL", "USD", …). Absent on older payloads,
    /// which format as <c>$</c> for back-compat.</summary>
    [JsonPropertyName("currency")]
    [JsonConverter(typeof(IsoCurrencyConverter))]
    public string? Currency { get; set; }

    /// <summary>Minor-unit digits (BRL/USD = 2, JPY/KRW = 0). Null means the
    /// wire did not report the scale — we don't guess it from the currency
    /// code alone.</summary>
    [JsonPropertyName("decimal_places")]
    [JsonConverter(typeof(LenientDecimalPlacesConverter))]
    public int? DecimalPlaces { get; set; }
}

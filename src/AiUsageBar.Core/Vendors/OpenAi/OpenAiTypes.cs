using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Json;
using AiUsageBar.Core.Models;

namespace AiUsageBar.Core.Vendors.OpenAi;

/// <summary>
/// Wire types for <c>GET https://chatgpt.com/backend-api/wham/usage</c>. Port of
/// the Rust <c>openai/types.rs</c>.
/// </summary>
public sealed class OpenAiUsageResponse
{
    [JsonPropertyName("plan_type")] public string? PlanType { get; set; }
    [JsonPropertyName("rate_limit")] public RateLimit? RateLimit { get; set; }
    [JsonPropertyName("code_review_rate_limit")] public RateLimit? CodeReviewRateLimit { get; set; }
    [JsonPropertyName("credits")] public CreditsBlock? Credits { get; set; }

    public static OpenAiUsageResponse Parse(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, AppJsonContext.Default.OpenAiUsageResponse) ?? new();

    public OpenAiSnapshot ToSnapshot(string? planHint)
    {
        var planType = PlanType ?? planHint ?? "Unknown";
        var plan = $"ChatGPT {Capitalize(planType)}";

        var rl = RateLimit ?? new RateLimit();
        var session = WindowOrDefault(rl.PrimaryWindow, TimeSpan.FromHours(5));
        var weekly = WindowOrDefault(rl.SecondaryWindow, TimeSpan.FromDays(7));
        UsageWindow? codeReview = CodeReviewRateLimit?.PrimaryWindow is { } cw
            ? ToWindow(cw, TimeSpan.FromDays(7)) : null;

        OpenAiCredits? credits = Credits is { } c
            ? new OpenAiCredits(c.Balance, c.HasCredits, c.Unlimited,
                RangeFromList(c.ApproxLocalMessages), RangeFromList(c.ApproxCloudMessages))
            : null;

        return new OpenAiSnapshot(plan, session, weekly, codeReview, credits, OpenAiSource.CodexOauth);
    }

    private static UsageWindow WindowOrDefault(OpenAiWindow? w, TimeSpan def) =>
        w is null ? new UsageWindow(0, null, def) : ToWindow(w, def);

    private static UsageWindow ToWindow(OpenAiWindow w, TimeSpan def)
    {
        var dur = w.LimitWindowSeconds > 0 ? TimeSpan.FromSeconds(w.LimitWindowSeconds) : def;
        DateTimeOffset? reset = w.ResetAt is { } secs
            ? DateTimeOffset.FromUnixTimeSeconds(secs)
            : w.ResetAfterSeconds is { } after
                ? DateTimeOffset.UtcNow + TimeSpan.FromSeconds(after)
                : null;
        return new UsageWindow((int)Math.Clamp(w.UsedPercent, 0, 100), reset, dur);
    }

    private static (long, long)? RangeFromList(List<long>? v)
    {
        if (v is null || v.Count == 0) return null;
        return v.Count >= 2 ? (v[0], v[1]) : (v[0], v[0]);
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

public sealed class RateLimit
{
    [JsonPropertyName("primary_window")] public OpenAiWindow? PrimaryWindow { get; set; }
    [JsonPropertyName("secondary_window")] public OpenAiWindow? SecondaryWindow { get; set; }
}

public sealed class OpenAiWindow
{
    [JsonPropertyName("used_percent")]
    [JsonConverter(typeof(LenientLongConverter))]
    public long UsedPercent { get; set; }

    [JsonPropertyName("limit_window_seconds")]
    [JsonConverter(typeof(LenientLongConverter))]
    public long LimitWindowSeconds { get; set; }

    [JsonPropertyName("reset_at")]
    [JsonConverter(typeof(LenientNullableLongConverter))]
    public long? ResetAt { get; set; }

    [JsonPropertyName("reset_after_seconds")]
    [JsonConverter(typeof(LenientNullableLongConverter))]
    public long? ResetAfterSeconds { get; set; }
}

public sealed class CreditsBlock
{
    [JsonPropertyName("balance")]
    [JsonConverter(typeof(MoneyStringConverter))]
    public string Balance { get; set; } = "$0.00";

    [JsonPropertyName("has_credits")] public bool HasCredits { get; set; }
    [JsonPropertyName("unlimited")] public bool Unlimited { get; set; }
    [JsonPropertyName("approx_local_messages")] public List<long>? ApproxLocalMessages { get; set; }
    [JsonPropertyName("approx_cloud_messages")] public List<long>? ApproxCloudMessages { get; set; }
}

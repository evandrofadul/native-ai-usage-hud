using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Json;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Vendors.OpenAi;

/// <summary>
/// Wire types for <c>GET https://chatgpt.com/backend-api/wham/usage</c>. Port of
/// the Rust <c>openai/types.rs</c>.
/// </summary>
public sealed class OpenAiUsageResponse
{
    /// <summary><c>limit_window_seconds</c> the API reports for the 5-hour window.</summary>
    private const long SessionWindowSecs = 18_000;
    /// <summary><c>limit_window_seconds</c> the API reports for the 7-day window.</summary>
    private const long WeeklyWindowSecs = 604_800;

    private enum WindowKind { Session, Weekly }

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

        var (session, weekly) = ClassifyRateLimit(RateLimit ?? new RateLimit());
        UsageWindow? codeReview = CodeReviewRateLimit?.PrimaryWindow is { } cw
            ? ToWindow(cw, TimeSpan.FromDays(7)) : null;

        OpenAiCredits? credits = Credits is { } c
            ? new OpenAiCredits(c.Balance, c.HasCredits, c.Unlimited,
                RangeFromList(c.ApproxLocalMessages), RangeFromList(c.ApproxCloudMessages))
            : null;

        return new OpenAiSnapshot(
            plan,
            session ?? new UsageWindow(0, null, TimeSpan.FromSeconds(SessionWindowSecs)),
            weekly ?? new UsageWindow(0, null, TimeSpan.FromSeconds(WeeklyWindowSecs)),
            codeReview, credits, OpenAiSource.CodexOauth);
    }

    /// <summary>
    /// Classify each window by its reported <c>limit_window_seconds</c> rather
    /// than its wire position — OpenAI has shipped payloads where the 7-day
    /// window arrives in <c>primary_window</c> with <c>secondary_window</c>
    /// omitted (openai/codex#32707), which would otherwise be mislabeled as the
    /// 5-hour session window. Wire position is only a fallback for a window
    /// whose duration doesn't match either known constant.
    /// </summary>
    private static (UsageWindow? Session, UsageWindow? Weekly) ClassifyRateLimit(RateLimit rl)
    {
        UsageWindow? session = null;
        UsageWindow? weekly = null;

        void Insert(OpenAiWindow? wire, WindowKind fallbackKind)
        {
            if (wire is null) return;
            var kind = ClassifyKind(wire) ?? fallbackKind;
            var defaultDuration = TimeSpan.FromSeconds(
                kind == WindowKind.Session ? SessionWindowSecs : WeeklyWindowSecs);
            var window = ToWindow(wire, defaultDuration);
            if (kind == WindowKind.Session)
            {
                if (session is not null) throw DuplicateWindowError(kind, wire.LimitWindowSeconds);
                session = window;
            }
            else
            {
                if (weekly is not null) throw DuplicateWindowError(kind, wire.LimitWindowSeconds);
                weekly = window;
            }
        }

        Insert(rl.PrimaryWindow, WindowKind.Session);
        Insert(rl.SecondaryWindow, WindowKind.Weekly);
        return (session, weekly);
    }

    private static WindowKind? ClassifyKind(OpenAiWindow w) => w.LimitWindowSeconds switch
    {
        SessionWindowSecs => WindowKind.Session,
        WeeklyWindowSecs => WindowKind.Weekly,
        _ => null,
    };

    private static SchemaException DuplicateWindowError(WindowKind kind, long seconds)
    {
        var label = kind == WindowKind.Session ? "5h" : "7d";
        return new SchemaException(
            $"duplicate OpenAI {label} window with limit_window_seconds={seconds}; expected at most one 5h and one 7d window");
    }

    private static UsageWindow ToWindow(OpenAiWindow w, TimeSpan def)
    {
        var dur = w.LimitWindowSeconds > 0 ? (SafeFromSeconds(w.LimitWindowSeconds) ?? def) : def;
        DateTimeOffset? reset = w.ResetAt is { } secs
            ? SafeFromUnixSeconds(secs)
            : w.ResetAfterSeconds is { } after
                ? SafeAddSeconds(DateTimeOffset.UtcNow, after)
                : null;
        return new UsageWindow((int)Math.Clamp(w.UsedPercent, 0, 100), reset, dur);
    }

    /// <summary>
    /// <see cref="TimeSpan.FromSeconds(long)"/> throws well before <see cref="long"/>'s
    /// range (TimeSpan tops out around 10,675,199 days). An absurd but
    /// validly-numeric counter must degrade to the caller's default duration,
    /// not crash the fetch.
    /// </summary>
    private static TimeSpan? SafeFromSeconds(long seconds)
    {
        try { return TimeSpan.FromSeconds(seconds); }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException) { return null; }
    }

    private static DateTimeOffset? SafeFromUnixSeconds(long secs)
    {
        try { return DateTimeOffset.FromUnixTimeSeconds(secs); }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException) { return null; }
    }

    private static DateTimeOffset? SafeAddSeconds(DateTimeOffset from, long seconds)
    {
        try { return SafeFromSeconds(seconds) is { } d ? from + d : null; }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException) { return null; }
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
    [JsonConverter(typeof(StrictCounterConverter))]
    public long UsedPercent { get; set; }

    [JsonPropertyName("limit_window_seconds")]
    [JsonConverter(typeof(StrictCounterConverter))]
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

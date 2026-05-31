using AiUsageBar.Core.Pacing;

namespace AiUsageBar.Core.Models;

/// <summary>
/// Base type for vendor-specific snapshots. Each vendor has a genuinely different
/// shape, so we use a sealed hierarchy and pattern-match on it in the renderers —
/// the C# equivalent of the Rust <c>VendorSnapshot</c> enum.
/// </summary>
public abstract record VendorSnapshot;

/// <summary>Anthropic — three rolling windows plus optional pay-as-you-go credits.</summary>
public sealed record AnthropicSnapshot(
    string Plan,
    UsageWindow Session,
    UsageWindow Weekly,
    UsageWindow? Sonnet,
    ExtraUsage? Extra) : VendorSnapshot
{
    /// <summary>
    /// Worst-of severity for the tray color. "Extra usage only matters when a rate
    /// limit hits 100%" — mirrors claudebar.
    /// </summary>
    public PaceSeverity Severity()
    {
        var max = Session.UtilizationPct;
        if (Weekly.UtilizationPct > max) max = Weekly.UtilizationPct;
        if (Sonnet is { } s && s.UtilizationPct > max) max = s.UtilizationPct;

        var anyAtCap = Session.UtilizationPct >= 100
            || Weekly.UtilizationPct >= 100
            || (Sonnet is { } sw && sw.UtilizationPct >= 100);
        if (anyAtCap && Extra is { } extra)
        {
            var p = extra.Percent();
            if (p > max) max = p;
        }
        return SeverityRules.SeverityFor(max);
    }
}

public enum OpenAiSource
{
    CodexOauth,
    AdminKeyMtd,
    Unavailable,
}

/// <summary>Credit balance + approximate message-count ranges (OpenAI Codex).</summary>
public sealed record OpenAiCredits(
    string Balance,
    bool HasCredits,
    bool Unlimited,
    (long Lo, long Hi)? ApproxLocalMessages,
    (long Lo, long Hi)? ApproxCloudMessages);

/// <summary>OpenAI Codex OAuth — two windows + optional code-review + credits.</summary>
public sealed record OpenAiSnapshot(
    string Plan,
    UsageWindow Session,
    UsageWindow Weekly,
    UsageWindow? CodeReview,
    OpenAiCredits? Credits,
    OpenAiSource Source) : VendorSnapshot;

/// <summary>One GitHub Copilot quota bucket (chat / completions / premium requests).</summary>
public sealed record CopilotQuota(
    string Name,
    int UtilizationPct,
    bool Unlimited,
    double Remaining,
    double Entitlement,
    long OverageCount);

/// <summary>
/// GitHub Copilot — plan + the monthly quota buckets returned by the
/// <c>copilot_internal/v2/token</c> exchange (premium requests, chat, completions).
/// </summary>
public sealed record CopilotSnapshot(
    string Plan,
    bool ChatEnabled,
    DateTimeOffset? ResetsAt,
    IReadOnlyList<CopilotQuota> Quotas) : VendorSnapshot
{
    /// <summary>Worst utilization across the metered (non-unlimited) quotas.</summary>
    public PaceSeverity Severity()
    {
        var max = 0;
        foreach (var q in Quotas)
            if (!q.Unlimited && q.UtilizationPct > max) max = q.UtilizationPct;
        return SeverityRules.SeverityFor(max);
    }
}

/// <summary>Z.AI / BigModel — projected token + MCP buckets.</summary>
public sealed record ZaiSnapshot(
    string Plan,
    UsageWindow? Session,
    UsageWindow? Weekly,
    UsageWindow? Mcp) : VendorSnapshot;

/// <summary>OpenRouter — credit balance + lifetime/daily/weekly/monthly usage.</summary>
public sealed record OpenRouterSnapshot(
    string Label,
    double TotalCredits,
    double TotalUsage,
    double UsageDaily,
    double UsageWeekly,
    double UsageMonthly,
    bool IsFreeTier,
    double? Limit,
    double? LimitRemaining) : VendorSnapshot
{
    public double Balance() => Math.Max(TotalCredits - TotalUsage, 0.0);

    /// <summary>Percentage of total_credits consumed (0..=100); 0 when no credits.</summary>
    public int ConsumedPct()
    {
        if (TotalCredits <= 0.0) return 0;
        return (int)Math.Clamp(Math.Round(TotalUsage / TotalCredits * 100.0), 0.0, 100.0);
    }
}

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

/// <summary>One Gemini CLI per-model quota bucket from <c>retrieveUserQuota</c>.</summary>
public sealed record GeminiQuota(
    string Model,
    int UtilizationPct,
    DateTimeOffset? ResetsAt,
    double? Remaining);

/// <summary>
/// Gemini CLI (Code Assist) — plan tier + the per-model daily quota buckets returned
/// by <c>cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota</c> (Flash / Flash
/// Lite / Pro …). Each bucket carries its own reset time. This is the same overall
/// shape as <see cref="CopilotSnapshot"/> — several metered buckets, worst-of severity.
/// </summary>
public sealed record GeminiSnapshot(
    string Plan,
    IReadOnlyList<GeminiQuota> Quotas) : VendorSnapshot
{
    /// <summary>Worst utilization across the quota buckets.</summary>
    public PaceSeverity Severity()
    {
        var max = 0;
        foreach (var q in Quotas)
            if (q.UtilizationPct > max) max = q.UtilizationPct;
        return SeverityRules.SeverityFor(max);
    }

    /// <summary>
    /// Collapse the per-model buckets into model variants (Flash / Flash Lite / Pro …),
    /// each represented by its worst (highest-utilization) bucket. Ordered by utilization,
    /// highest first. Used when <c>[gemini].group_by_variant</c> is on.
    /// </summary>
    public IReadOnlyList<GeminiQuota> GroupedByVariant() =>
        Quotas
            .GroupBy(q => VariantOf(q.Model))
            .Select(g => g.MaxBy(q => q.UtilizationPct)! with { Model = g.Key })
            .OrderByDescending(q => q.UtilizationPct)
            .ThenBy(q => q.Model, StringComparer.Ordinal)
            .ToList();

    /// <summary>Map a Gemini model id to its variant label (its tier).</summary>
    public static string VariantOf(string model)
    {
        var m = model.ToLowerInvariant();
        if (m.Contains("flash-lite") || m.Contains("flash_lite")) return "Flash Lite";
        if (m.Contains("flash")) return "Flash";
        if (m.Contains("pro")) return "Pro";
        return model; // unknown tier — keep the raw id as its own group
    }
}


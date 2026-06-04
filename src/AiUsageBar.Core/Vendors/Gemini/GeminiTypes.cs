using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Json;
using AiUsageBar.Core.Models;

namespace AiUsageBar.Core.Vendors.Gemini;

// ---- loadCodeAssist (resolves the managed project id + plan tier) ----

/// <summary>Client metadata sent with <c>:loadCodeAssist</c> (mirrors the Gemini CLI).</summary>
public sealed class GeminiClientMetadata
{
    [JsonPropertyName("ideType")] public string IdeType { get; set; } = "IDE_UNSPECIFIED";
    [JsonPropertyName("platform")] public string Platform { get; set; } = "PLATFORM_UNSPECIFIED";
    [JsonPropertyName("pluginType")] public string PluginType { get; set; } = "GEMINI";
}

/// <summary>Body for <c>POST /v1internal:loadCodeAssist</c>.</summary>
public sealed class GeminiLoadRequest
{
    [JsonPropertyName("cloudaicompanionProject")] public string? CloudaicompanionProject { get; set; }
    [JsonPropertyName("metadata")] public GeminiClientMetadata Metadata { get; set; } = new();
}

/// <summary>One plan tier from the <c>loadCodeAssist</c> response.</summary>
public sealed class GeminiTier
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

/// <summary>Response from <c>:loadCodeAssist</c> — carries the managed project + tier.</summary>
public sealed class GeminiLoadResponse
{
    [JsonPropertyName("cloudaicompanionProject")] public string? CloudaicompanionProject { get; set; }
    [JsonPropertyName("currentTier")] public GeminiTier? CurrentTier { get; set; }

    public static GeminiLoadResponse Parse(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, AppJsonContext.Default.GeminiLoadResponse) ?? new();

    /// <summary>Plan label "Gemini &lt;Tier&gt;" derived from the current tier.</summary>
    public string PlanLabel()
    {
        // The API's tier name is already a full label (e.g. "Gemini Code Assist"); the
        // id fallbacks ("Free"/"Standard"/…) are bare, so only those get the prefix.
        var tier = CurrentTier?.Name;
        if (string.IsNullOrWhiteSpace(tier))
            tier = CurrentTier?.Id switch
            {
                "free-tier" => "Free",
                "legacy-tier" => "Legacy",
                "standard-tier" => "Standard",
                _ => null,
            };
        if (string.IsNullOrWhiteSpace(tier)) return "Gemini";
        return tier.Contains("Gemini", StringComparison.OrdinalIgnoreCase) ? tier : $"Gemini {tier}";
    }
}

// ---- retrieveUserQuota (the per-model usage buckets) ----

/// <summary>Body for <c>POST /v1internal:retrieveUserQuota</c>.</summary>
public sealed class GeminiQuotaRequest
{
    [JsonPropertyName("project")] public string Project { get; set; } = "";
}

/// <summary>One quota bucket from <c>retrieveUserQuota</c>.</summary>
public sealed class GeminiBucket
{
    [JsonPropertyName("modelId")] public string? ModelId { get; set; }
    [JsonPropertyName("tokenType")] public string? TokenType { get; set; }
    [JsonPropertyName("remainingFraction")] public double? RemainingFraction { get; set; }
    [JsonPropertyName("remainingAmount")] public string? RemainingAmount { get; set; }
    [JsonPropertyName("resetTime")] public string? ResetTime { get; set; }
}

/// <summary>
/// Wire type for <c>retrieveUserQuota</c>. Buckets without a <c>modelId</c> or
/// <c>remainingFraction</c> are ignored (same guard the Gemini CLI uses). Unknown
/// members are skipped.
/// </summary>
public sealed class GeminiQuotaResponse
{
    [JsonPropertyName("buckets")] public List<GeminiBucket>? Buckets { get; set; }

    public static GeminiQuotaResponse Parse(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, AppJsonContext.Default.GeminiQuotaResponse) ?? new();

    public GeminiSnapshot ToSnapshot(string plan)
    {
        var quotas = new List<GeminiQuota>();
        if (Buckets is not null)
            foreach (var b in Buckets)
            {
                if (string.IsNullOrEmpty(b.ModelId) || b.RemainingFraction is not { } frac) continue;

                var used = (int)Math.Clamp(Math.Round((1.0 - frac) * 100.0), 0, 100);
                double? remaining = double.TryParse(b.RemainingAmount, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var r) ? r : null;
                quotas.Add(new GeminiQuota(b.ModelId!, used, ParseReset(b.ResetTime), remaining));
            }

        return new GeminiSnapshot(plan, quotas);
    }

    private static DateTimeOffset? ParseReset(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;
        return DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto : null;
    }
}

/// <summary>
/// Cache representation for Gemini. The OAuth access token is short-lived and
/// sensitive, so we persist only the derived snapshot — same approach as Copilot.
/// </summary>
public sealed class GeminiCacheDto
{
    public string Plan { get; set; } = "Gemini";
    public List<GeminiQuota> Quotas { get; set; } = [];

    public static byte[] Serialize(GeminiSnapshot s) =>
        JsonSerializer.SerializeToUtf8Bytes(new GeminiCacheDto
        {
            Plan = s.Plan,
            Quotas = s.Quotas.ToList(),
        }, AppJsonContext.Default.GeminiCacheDto);

    public static GeminiSnapshot Deserialize(byte[] bytes)
    {
        var d = JsonSerializer.Deserialize(bytes, AppJsonContext.Default.GeminiCacheDto) ?? new();
        return new GeminiSnapshot(d.Plan, d.Quotas);
    }
}

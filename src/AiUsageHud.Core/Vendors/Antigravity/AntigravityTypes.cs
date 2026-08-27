using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Json;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Vendors.Antigravity;

// ---- GetUserStatus wire types ----

public sealed class AntigravityUserTier
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

public sealed class AntigravityPlanInfo
{
    [JsonPropertyName("planName")] public string? PlanName { get; set; }
}

public sealed class AntigravityPlanStatus
{
    [JsonPropertyName("planInfo")] public AntigravityPlanInfo? PlanInfo { get; set; }
}

public sealed class AntigravityUserStatusBlock
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("userTier")] public AntigravityUserTier? UserTier { get; set; }
    [JsonPropertyName("planStatus")] public AntigravityPlanStatus? PlanStatus { get; set; }
}

public sealed class AntigravityUserStatusResponse
{
    [JsonPropertyName("userStatus")] public AntigravityUserStatusBlock? UserStatus { get; set; }

    public static AntigravityUserStatusResponse Parse(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, AppJsonContext.Default.AntigravityUserStatusResponse) ?? new();

    public string PlanLabel()
    {
        var tier = UserStatus?.UserTier?.Name;
        if (string.IsNullOrWhiteSpace(tier))
            tier = UserStatus?.UserTier?.Description;
        if (string.IsNullOrWhiteSpace(tier))
            tier = UserStatus?.PlanStatus?.PlanInfo?.PlanName;

        return !string.IsNullOrWhiteSpace(tier) ? tier : "Antigravity";
    }

    public string AccountKey()
    {
        var email = UserStatus?.Email?.Trim();
        if (string.IsNullOrEmpty(email)) return "acct:unknown";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(email));
        var hex = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        return $"acct:{hex}";
    }
}

// ---- RetrieveUserQuotaSummary wire types ----

public sealed class AntigravityBucket
{
    [JsonPropertyName("bucketId")] public string? BucketId { get; set; }
    [JsonPropertyName("window")] public string? Window { get; set; }
    [JsonPropertyName("remainingFraction")] public double? RemainingFraction { get; set; }
    [JsonPropertyName("resetTime")] public string? ResetTime { get; set; }
}

public sealed class AntigravityGroup
{
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("buckets")] public List<AntigravityBucket>? Buckets { get; set; }
}

public sealed class AntigravityQuotaEnvelope
{
    [JsonPropertyName("groups")] public List<AntigravityGroup>? Groups { get; set; }
}

public sealed class AntigravityQuotaResponse
{
    [JsonPropertyName("response")] public AntigravityQuotaEnvelope? Response { get; set; }
    [JsonPropertyName("groups")] public List<AntigravityGroup>? DirectGroups { get; set; }

    public List<AntigravityGroup>? AllGroups => Response?.Groups ?? DirectGroups;

    public static AntigravityQuotaResponse Parse(byte[] bytes) =>
        JsonSerializer.Deserialize(bytes, AppJsonContext.Default.AntigravityQuotaResponse) ?? new();

    public AntigravitySnapshot ToSnapshot(string plan, string account = "")
    {
        var groups = AllGroups
            ?? throw new OtherException("antigravity: quota summary has no groups");

        UsageWindow? gemini5h = null;
        UsageWindow? geminiWeekly = null;
        UsageWindow? tp5h = null;
        UsageWindow? tpWeekly = null;

        foreach (var group in groups)
        {
            var groupName = group.DisplayName ?? "";
            if (group.Buckets is null) continue;

            foreach (var bucket in group.Buckets)
            {
                var id = bucket.BucketId ?? "";
                var win = bucket.Window ?? "";

                bool isWeekly;
                if (id.EndsWith("weekly", StringComparison.OrdinalIgnoreCase) || win.Equals("weekly", StringComparison.OrdinalIgnoreCase))
                {
                    isWeekly = true;
                }
                else if (id.EndsWith("5h", StringComparison.OrdinalIgnoreCase) || win.Equals("5h", StringComparison.OrdinalIgnoreCase))
                {
                    isWeekly = false;
                }
                else
                {
                    continue; // Unknown cadence — ignore
                }

                bool isGemini;
                if (id.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
                {
                    isGemini = true;
                }
                else if (id.StartsWith("3p", StringComparison.OrdinalIgnoreCase))
                {
                    isGemini = false;
                }
                else if (groupName.Contains("Gemini", StringComparison.OrdinalIgnoreCase))
                {
                    isGemini = true;
                }
                else if (groupName.Contains("Claude", StringComparison.OrdinalIgnoreCase) || groupName.Contains("GPT", StringComparison.OrdinalIgnoreCase))
                {
                    isGemini = false;
                }
                else
                {
                    continue; // Unknown group — ignore
                }

                var (current, slotName) = (isGemini, isWeekly) switch
                {
                    (true, false) => (gemini5h, "Gemini 5h"),
                    (true, true) => (geminiWeekly, "Gemini weekly"),
                    (false, false) => (tp5h, "Claude/GPT 5h"),
                    (false, true) => (tpWeekly, "Claude/GPT weekly"),
                };

                if (current is not null)
                    throw new SchemaException($"antigravity: duplicate {slotName} bucket");

                var parsedWindow = ParseWindow(bucket, isWeekly);

                switch (isGemini, isWeekly)
                {
                    case (true, false): gemini5h = parsedWindow; break;
                    case (true, true): geminiWeekly = parsedWindow; break;
                    case (false, false): tp5h = parsedWindow; break;
                    case (false, true): tpWeekly = parsedWindow; break;
                }
            }
        }

        var session = gemini5h ?? throw new OtherException("antigravity: quota summary has no Gemini 5h bucket");
        var weekly = geminiWeekly ?? throw new OtherException("antigravity: quota summary has no Gemini weekly bucket");

        return new AntigravitySnapshot(plan, account, session, weekly, tp5h, tpWeekly);
    }

    private static UsageWindow ParseWindow(AntigravityBucket bucket, bool isWeekly)
    {
        if (bucket.RemainingFraction is not { } frac || !double.IsFinite(frac) || frac < 0.0 || frac > 1.0)
        {
            throw new SchemaException(
                $"antigravity: bucket {bucket.BucketId ?? "<unnamed>"} has no valid remainingFraction in 0..=1");
        }

        var used = (int)Math.Clamp(Math.Round((1.0 - frac) * 100.0), 0, 100);
        var dur = isWeekly ? TimeSpan.FromDays(7) : TimeSpan.FromHours(5);
        var reset = ParseReset(bucket.ResetTime);

        return new UsageWindow(used, reset, dur);
    }

    private static DateTimeOffset? ParseReset(string? resetTime)
    {
        if (string.IsNullOrWhiteSpace(resetTime)) return null;
        if (DateTimeOffset.TryParse(resetTime, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto;
        }
        throw new SchemaException("antigravity: invalid quota resetTime");
    }
}

// ---- Cache DTO ----

public sealed class AntigravityCacheDto
{
    public string Plan { get; set; } = "Antigravity";
    public string Account { get; set; } = "";
    public UsageWindow Session { get; set; } = new(0, null, TimeSpan.FromHours(5));
    public UsageWindow Weekly { get; set; } = new(0, null, TimeSpan.FromDays(7));
    public UsageWindow? ThirdPartySession { get; set; }
    public UsageWindow? ThirdPartyWeekly { get; set; }

    public static byte[] Serialize(AntigravitySnapshot s) =>
        JsonSerializer.SerializeToUtf8Bytes(new AntigravityCacheDto
        {
            Plan = s.Plan,
            Account = s.Account,
            Session = s.Session,
            Weekly = s.Weekly,
            ThirdPartySession = s.ThirdPartySession,
            ThirdPartyWeekly = s.ThirdPartyWeekly,
        }, AppJsonContext.Default.AntigravityCacheDto);

    public static AntigravitySnapshot Deserialize(byte[] bytes)
    {
        var d = JsonSerializer.Deserialize(bytes, AppJsonContext.Default.AntigravityCacheDto) ?? new();
        return new AntigravitySnapshot(d.Plan, d.Account, d.Session, d.Weekly, d.ThirdPartySession, d.ThirdPartyWeekly);
    }
}

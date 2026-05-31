using System.Text;
using AiUsageBar.Core.Vendors.Anthropic;
using AiUsageBar.Core.Vendors.Copilot;
using AiUsageBar.Core.Vendors.OpenAi;
using AiUsageBar.Core.Vendors.OpenRouter;
using AiUsageBar.Core.Vendors.Zai;
using Xunit;

namespace AiUsageBar.Core.Tests;

public class VendorTypesTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    // ---- Anthropic ----

    [Fact]
    public void AnthropicParsesFullResponse()
    {
        var snap = AnthropicUsageResponse.Parse(B("""
            {
              "five_hour":        {"utilization": 42.7, "resets_at": "2026-05-23T17:30:00Z"},
              "seven_day":        {"utilization": 27.0, "resets_at": "2026-05-30T12:00:00Z"},
              "seven_day_sonnet": {"utilization":  4.2, "resets_at": "2026-05-30T12:00:00Z"},
              "extra_usage":      {"is_enabled": true, "monthly_limit": 5000, "used_credits": 250}
            }
            """)).ToSnapshot("Max 5x");
        Assert.Equal(43, snap.Session.UtilizationPct);
        Assert.Equal(27, snap.Weekly.UtilizationPct);
        Assert.Equal(4, snap.Sonnet!.Value.UtilizationPct);
        Assert.Equal(5000, snap.Extra!.Value.Limit.Value);
        Assert.Equal(250, snap.Extra.Value.Spent.Value);
        Assert.NotNull(snap.Session.ResetsAt);
    }

    [Fact]
    public void AnthropicMissingSonnetAndExtraAreNull()
    {
        var snap = AnthropicUsageResponse.Parse(B("""
            {"five_hour":{"utilization":0,"resets_at":"2026-05-23T17:30:00Z"},
             "seven_day":{"utilization":0,"resets_at":"2026-05-30T12:00:00Z"}}
            """)).ToSnapshot("Pro");
        Assert.Null(snap.Sonnet);
        Assert.Null(snap.Extra);
    }

    [Fact]
    public void AnthropicDisabledExtraBecomesNull()
    {
        var snap = AnthropicUsageResponse.Parse(B("""
            {"five_hour":{"utilization":0},"seven_day":{"utilization":0},
             "extra_usage":{"is_enabled":false,"monthly_limit":5000,"used_credits":0}}
            """)).ToSnapshot("Pro");
        Assert.Null(snap.Extra);
    }

    [Fact]
    public void AnthropicEmptyObjectYieldsNeutral()
    {
        var snap = AnthropicUsageResponse.Parse(B("{}")).ToSnapshot("Unknown");
        Assert.Equal(0, snap.Session.UtilizationPct);
        Assert.Null(snap.Session.ResetsAt);
    }

    [Fact]
    public void AnthropicUnparseableResetBecomesNull()
    {
        var snap = AnthropicUsageResponse.Parse(B("""
            {"five_hour":{"utilization":50,"resets_at":"not a date"},"seven_day":{"utilization":0}}
            """)).ToSnapshot("Pro");
        Assert.Null(snap.Session.ResetsAt);
        Assert.Equal(50, snap.Session.UtilizationPct);
    }

    [Fact]
    public void AnthropicPlanLabels()
    {
        Assert.Equal("Max 5x", ParseCreds("max", "default_claude_max_5x").PlanLabel());
        Assert.Equal("Max 20x", ParseCreds("max", "default_claude_max_20x").PlanLabel());
        Assert.Equal("Pro", ParseCreds("pro", "").PlanLabel());
        Assert.Equal("Unknown", ParseCreds("", "").PlanLabel());
    }

    private static OauthCreds ParseCreds(string sub, string tier) =>
        new() { SubscriptionType = sub, RateLimitTier = tier };

    // ---- OpenAI ----

    [Fact]
    public void OpenAiParsesRealShape()
    {
        var snap = OpenAiUsageResponse.Parse(B("""
            {"plan_type":"plus","rate_limit":{
              "primary_window":{"used_percent":1,"limit_window_seconds":18000,"reset_at":1779597324},
              "secondary_window":{"used_percent":0,"limit_window_seconds":604800,"reset_at":1780184124}}}
            """)).ToSnapshot(null);
        Assert.Equal("ChatGPT Plus", snap.Plan);
        Assert.Equal(1, snap.Session.UtilizationPct);
        Assert.Equal(0, snap.Weekly.UtilizationPct);
        Assert.Equal(TimeSpan.FromHours(5), snap.Session.WindowDuration);
        Assert.Equal(TimeSpan.FromDays(7), snap.Weekly.WindowDuration);
        Assert.Null(snap.CodeReview);
        Assert.Null(snap.Credits);
    }

    [Fact]
    public void OpenAiUsedPercentClampsAndCreditsParse()
    {
        var snap = OpenAiUsageResponse.Parse(B("""
            {"plan_type":"plus",
             "rate_limit":{"primary_window":{"used_percent":250,"limit_window_seconds":1}},
             "credits":{"balance":"$2.50","has_credits":true,"unlimited":false,
                "approx_local_messages":[100,200],"approx_cloud_messages":[40,60]}}
            """)).ToSnapshot(null);
        Assert.Equal(100, snap.Session.UtilizationPct);
        Assert.Equal("$2.50", snap.Credits!.Balance);
        Assert.Equal((100L, 200L), snap.Credits.ApproxLocalMessages);
        Assert.Equal((40L, 60L), snap.Credits.ApproxCloudMessages);
    }

    [Fact]
    public void OpenAiBalanceAsNumberFormats()
    {
        var snap = OpenAiUsageResponse.Parse(B(
            """{"credits":{"balance":1.5,"has_credits":true,"unlimited":false}}""")).ToSnapshot(null);
        Assert.Equal("$1.50", snap.Credits!.Balance);
    }

    [Fact]
    public void OpenAiPlanHintUsedWhenOmitted()
    {
        var snap = OpenAiUsageResponse.Parse(B("{}")).ToSnapshot("team");
        Assert.Equal("ChatGPT Team", snap.Plan);
    }

    [Fact]
    public void OpenAiCredsExtractsPlanAndExpFromJwt()
    {
        var jwt = FakeJwt("""{"exp":1234567890,"https://api.openai.com/auth":{"chatgpt_plan_type":"plus"}}""");
        var t = new Tokens { AccessToken = "AT", RefreshToken = "RT", IdToken = jwt };
        Assert.Equal(1234567890, t.ExpiresAtSecs);
        Assert.Equal("plus", t.PlanTypeFromIdToken());
    }

    [Fact]
    public void OpenAiMalformedJwtReturnsZeroExp()
    {
        var t = new Tokens { IdToken = "not.a.jwt" };
        Assert.Equal(0, t.ExpiresAtSecs);
        Assert.Null(t.PlanTypeFromIdToken());
    }

    private static string FakeJwt(string claims)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("""{"alg":"none"}""")}.{B64(claims)}.sig";
    }

    // ---- Z.AI ----

    [Fact]
    public void ZaiParsesRealShape()
    {
        var snap = ZaiEnvelope.Parse(B("""
            {"code":200,"msg":"ok","data":{"limits":[
              {"type":"TOKENS_LIMIT","percentage":0},
              {"type":"TOKENS_LIMIT","percentage":0,"nextResetTime":1779792169974},
              {"type":"TIME_LIMIT","percentage":0,"nextResetTime":1779964969979}
            ],"level":"pro"},"success":true}
            """)).ToSnapshot(null);
        Assert.Equal("GLM Coding Pro", snap.Plan);
        Assert.NotNull(snap.Session);
        Assert.NotNull(snap.Weekly);
        Assert.NotNull(snap.Mcp);
        Assert.NotNull(snap.Weekly!.Value.ResetsAt);
    }

    [Fact]
    public void ZaiPercentageRoundsAndClamps()
    {
        var s1 = ZaiEnvelope.Parse(B("""{"data":{"limits":[{"type":"TOKENS_LIMIT","percentage":42.7}],"level":"max"}}""")).ToSnapshot(null);
        Assert.Equal(43, s1.Session!.Value.UtilizationPct);
        var s2 = ZaiEnvelope.Parse(B("""{"data":{"limits":[{"type":"TOKENS_LIMIT","percentage":150}]}}""")).ToSnapshot(null);
        Assert.Equal(100, s2.Session!.Value.UtilizationPct);
    }

    [Fact]
    public void ZaiConfigTierUsedWhenLevelEmpty()
    {
        var snap = ZaiEnvelope.Parse(B("""{"data":{"limits":[],"level":""},"success":true}""")).ToSnapshot("max");
        Assert.Equal("GLM Coding Max", snap.Plan);
    }

    [Fact]
    public void ZaiOnlyTimeLimitMeansNoSessionOrWeekly()
    {
        var snap = ZaiEnvelope.Parse(B("""{"data":{"limits":[{"type":"TIME_LIMIT","percentage":12}]}}""")).ToSnapshot(null);
        Assert.Null(snap.Session);
        Assert.Null(snap.Weekly);
        Assert.NotNull(snap.Mcp);
    }

    // ---- OpenRouter ----

    [Fact]
    public void OpenRouterCombineBuildsSnapshot()
    {
        var c = OpenRouterMapping.ParseCredits("""{"data":{"total_credits":100.0,"total_usage":30.0}}""");
        var k = OpenRouterMapping.ParseKey("""
            {"data":{"label":"key-A","limit":50.0,"limit_remaining":20.0,
             "usage":30.0,"usage_daily":1.0,"usage_weekly":5.0,"usage_monthly":30.0,"is_free_tier":false}}
            """);
        var snap = OpenRouterMapping.Combine(c, k);
        Assert.Equal("OpenRouter — key-A", snap.Label);
        Assert.Equal(70.0, snap.Balance(), 9);
        Assert.Equal(30, snap.ConsumedPct());
        Assert.Equal(30.0, snap.UsageMonthly);
    }

    [Fact]
    public void OpenRouterEmptyLabelDefaults()
    {
        var snap = OpenRouterMapping.Combine(new(), new());
        Assert.Equal("OpenRouter", snap.Label);
    }

    [Fact]
    public void OpenRouterCacheRoundTrips()
    {
        var snap = new Core.Models.OpenRouterSnapshot("OpenRouter — k", 100, 25, 1, 5, 25, false, 50, 25);
        var back = OpenRouterCacheDto.Deserialize(OpenRouterCacheDto.Serialize(snap));
        Assert.Equal(snap, back);
    }

    // ---- Copilot ----

    [Fact]
    public void CopilotParsesPlanAndQuotas()
    {
        var snap = CopilotUserResponse.Parse(B("""
            {"copilot_plan":"individual","chat_enabled":true,"quota_reset_date":"2026-06-01",
             "quota_snapshots":{
               "chat":{"entitlement":0,"remaining":0,"percent_remaining":100,"unlimited":true},
               "premium_interactions":{"entitlement":300,"remaining":225,"percent_remaining":75,"unlimited":false,"overage_count":2}
             }}
            """)).ToSnapshot();

        Assert.Equal("Copilot Individual", snap.Plan);
        Assert.True(snap.ChatEnabled);
        Assert.NotNull(snap.ResetsAt);
        // Premium requests are ordered first; 75% remaining → 25% utilized.
        var premium = snap.Quotas[0];
        Assert.Equal("Premium requests", premium.Name);
        Assert.Equal(25, premium.UtilizationPct);
        Assert.False(premium.Unlimited);
        Assert.Equal(2, premium.OverageCount);
        Assert.Contains(snap.Quotas, q => q is { Name: "Chat", Unlimited: true });
    }

    [Fact]
    public void CopilotEmptyResponseYieldsNoQuotas()
    {
        var snap = CopilotUserResponse.Parse(B("{}")).ToSnapshot();
        Assert.Equal("Copilot Unknown", snap.Plan);
        Assert.Empty(snap.Quotas);
        Assert.Null(snap.ResetsAt);
    }

    [Fact]
    public void CopilotCacheRoundTrips()
    {
        var snap = new Core.Models.CopilotSnapshot("Copilot Business", true,
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            [new Core.Models.CopilotQuota("Premium requests", 40, false, 180, 300, 0)]);
        var back = CopilotCacheDto.Deserialize(CopilotCacheDto.Serialize(snap));

        Assert.Equal(snap.Plan, back.Plan);
        Assert.Equal(snap.ChatEnabled, back.ChatEnabled);
        Assert.Equal(snap.ResetsAt, back.ResetsAt);
        Assert.Equal(snap.Quotas, back.Quotas); // CopilotQuota is a scalar record → element-wise equality
    }

    [Fact]
    public void CopilotExtractsTokenFromUtf8AndUtf16Blobs()
    {
        const string token = "gho_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        Assert.Equal(token, CopilotCreds.ExtractToken(Encoding.UTF8.GetBytes(token)));
        Assert.Equal(token, CopilotCreds.ExtractToken(Encoding.Unicode.GetBytes(token)));
        Assert.Null(CopilotCreds.ExtractToken(Encoding.UTF8.GetBytes("no token here")));
    }
}

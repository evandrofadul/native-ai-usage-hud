using System.Text;
using System.Text.Json;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Vendors.Anthropic;
using AiUsageHud.Core.Vendors.Copilot;
using AiUsageHud.Core.Vendors.Gemini;
using AiUsageHud.Core.Vendors.OpenAi;

namespace AiUsageHud.Core.Tests;

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
        Assert.Equal(5000, snap.Extra!.Value.Limit!.Value.Value);
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
    public void AnthropicBenignOvershootSaturatesToHundred()
    {
        // A hair over the cap is rounding noise, not drift — it must render as
        // a full bar, not fail the parse.
        var snap = AnthropicUsageResponse.Parse(B("""
            {"five_hour":{"utilization":100.4},"seven_day":{"utilization":100.6}}
            """)).ToSnapshot("Pro");
        Assert.Equal(100, snap.Session.UtilizationPct);
        Assert.Equal(100, snap.Weekly.UtilizationPct); // rounds to 101, saturated
    }

    [Fact]
    public void AnthropicOutOfRangeUtilizationIsRejectedNotClamped()
    {
        // A value this far outside 0..100 is schema drift — clamping it would
        // render a confident bar for a number the API never really sent as a
        // percentage.
        foreach (var raw in new[]
        {
            """{"five_hour":{"utilization":500}}""",
            """{"five_hour":{"utilization":-1}}""",
            """{"seven_day":{"utilization":101.5}}""",
        })
        {
            Assert.Throws<JsonException>(() => AnthropicUsageResponse.Parse(B(raw)));
        }
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

    [Fact]
    public void AnthropicExtraUsageWithNullLimitKeepsSpendVisibleInItsCurrency()
    {
        // Issue #30: `monthly_limit: null` means "no cap" (e.g. Claude Pro), not
        // drift — the real credit spend must stay visible, in its own currency.
        var snap = AnthropicUsageResponse.Parse(B("""
            {"five_hour":{"utilization":0},"seven_day":{"utilization":0},
             "extra_usage":{"is_enabled":true,"monthly_limit":null,
                "used_credits":14157.0,"currency":"BRL","decimal_places":2}}
            """)).ToSnapshot("Pro");

        var extra = snap.Extra!.Value;
        Assert.Null(extra.Limit);
        Assert.Equal(14157, extra.Spent.Value);
        Assert.Equal(0, extra.Percent()); // no denominator → no invented percentage
        Assert.Equal("R$141.57", extra.FmtSpent());
        Assert.Null(extra.FmtLimit());
    }

    [Fact]
    public void AnthropicExtraUsageWithoutSpendIsDropped()
    {
        var snap = AnthropicUsageResponse.Parse(B("""
            {"five_hour":{"utilization":0},"seven_day":{"utilization":0},
             "extra_usage":{"is_enabled":true,"monthly_limit":5000}}
            """)).ToSnapshot("Pro");
        Assert.Null(snap.Extra);
    }

    [Fact]
    public void AnthropicExtraUsageLegacyPayloadFormatsAsDollars()
    {
        var snap = AnthropicUsageResponse.Parse(B("""
            {"five_hour":{"utilization":0},"seven_day":{"utilization":0},
             "extra_usage":{"is_enabled":true,"monthly_limit":5000,"used_credits":250}}
            """)).ToSnapshot("Pro");
        var extra = snap.Extra!.Value;
        Assert.Equal("$2.50", extra.FmtSpent());
        Assert.Equal("$50.00", extra.FmtLimit());
    }

    [Fact]
    public void AnthropicImplausibleDecimalPlacesFailsParse()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => AnthropicUsageResponse.Parse(B("""
            {"extra_usage":{"is_enabled":true,"used_credits":250,"decimal_places":100}}
            """)));
    }

    [Fact]
    public void AnthropicNonIsoCurrencyFailsParse()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => AnthropicUsageResponse.Parse(B("""
            {"extra_usage":{"is_enabled":true,"used_credits":250,"currency":"nope"}}
            """)));
    }

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
    public void OpenAiCountersAcceptIntFloatAndNumericString()
    {
        var snap = OpenAiUsageResponse.Parse(B("""
            {"rate_limit":{"primary_window":{"used_percent":"42","limit_window_seconds":"18000.9"}}}
            """)).ToSnapshot(null);
        Assert.Equal(42, snap.Session.UtilizationPct);
        Assert.Equal(TimeSpan.FromSeconds(18000), snap.Session.WindowDuration);
    }

    [Fact]
    public void OpenAiNullOrNonNumericCounterFailsParse()
    {
        foreach (var raw in new[]
        {
            """{"rate_limit":{"primary_window":{"used_percent":null,"limit_window_seconds":1}}}""",
            """{"rate_limit":{"primary_window":{"used_percent":"n/a","limit_window_seconds":1}}}""",
            """{"rate_limit":{"primary_window":{"used_percent":true,"limit_window_seconds":1}}}""",
        })
        {
            // A drifted counter must fail the whole response, not cache a
            // fabricated 0% bar.
            Assert.Throws<JsonException>(() => OpenAiUsageResponse.Parse(B(raw)));
        }
    }

    [Fact]
    public void OpenAiOmittedCountersStillDefaultToZero()
    {
        // Deliberate boundary: an *absent* counter defaults to 0 like any other
        // missing field. Only a present-but-wrong value counts as drift.
        var snap = OpenAiUsageResponse.Parse(B("{}")).ToSnapshot(null);
        Assert.Equal(0, snap.Session.UtilizationPct);
    }

    [Fact]
    public void OpenAiOversizedWindowSecondsDegradesInsteadOfThrowing()
    {
        // i64::MAX is a valid integer, so it clears the counter gate — but
        // TimeSpan.FromSeconds throws OverflowException on it, and a crashing
        // fetch must degrade to the default duration instead of failing outright.
        var snap = OpenAiUsageResponse.Parse(B("""
            {"rate_limit":{"primary_window":{
              "used_percent":1,"limit_window_seconds":9223372036854775807,
              "reset_after_seconds":9223372036854775807}}}
            """)).ToSnapshot(null);
        Assert.Equal(TimeSpan.FromHours(5), snap.Session.WindowDuration);
        Assert.Null(snap.Session.ResetsAt);
    }

    [Fact]
    public void OpenAiResetAtAsStringParses()
    {
        var snap = OpenAiUsageResponse.Parse(B("""
            {"rate_limit":{"primary_window":{"used_percent":1,"reset_at":"1779597324"}}}
            """)).ToSnapshot(null);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1779597324), snap.Session.ResetsAt);
    }

    [Fact]
    public void OpenAiBalanceAsNumberFormats()
    {
        var snap = OpenAiUsageResponse.Parse(B(
            """{"credits":{"balance":1.5,"has_credits":true,"unlimited":false}}""")).ToSnapshot(null);
        Assert.Equal("$1.50", snap.Credits!.Balance);
    }

    [Fact]
    public void OpenAiWeeklyOnlyPrimaryWindowIsNotMislabeledAsSession()
    {
        // openai/codex#32707: the API has shipped the 7-day window in
        // `primary_window` with `secondary_window` omitted. Classification must
        // key off the reported duration, not the wire position.
        var snap = OpenAiUsageResponse.Parse(B("""
            {"rate_limit":{
              "primary_window":{"used_percent":41,"limit_window_seconds":604800},
              "secondary_window":null}}
            """)).ToSnapshot(null);
        Assert.Equal(41, snap.Weekly.UtilizationPct);
        Assert.Equal(TimeSpan.FromDays(7), snap.Weekly.WindowDuration);
        Assert.Equal(0, snap.Session.UtilizationPct); // no session window reported
    }

    [Fact]
    public void OpenAiDuplicateWindowKindFailsParse()
    {
        var snap = OpenAiUsageResponse.Parse(B("""
            {"rate_limit":{
              "primary_window":{"used_percent":41,"limit_window_seconds":604800},
              "secondary_window":{"used_percent":7,"limit_window_seconds":604800}}}
            """));
        Assert.Throws<SchemaException>(() => snap.ToSnapshot(null));
    }

    [Fact]
    public void OpenAiUnrecognizedDurationFallsBackToWirePosition()
    {
        var snap = OpenAiUsageResponse.Parse(B("""
            {"rate_limit":{
              "primary_window":{"used_percent":10,"limit_window_seconds":3600},
              "secondary_window":{"used_percent":20,"limit_window_seconds":3600}}}
            """)).ToSnapshot(null);
        Assert.Equal(10, snap.Session.UtilizationPct);
        Assert.Equal(20, snap.Weekly.UtilizationPct);
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

    // ---- Gemini ----

    [Fact]
    public void GeminiParsesBucketsAndMapsUtilization()
    {
        var snap = GeminiQuotaResponse.Parse(B("""
            {"buckets":[
              {"modelId":"gemini-2.5-flash","tokenType":"REQUESTS","remainingFraction":0.99,
               "remainingAmount":"990","resetTime":"2026-06-03T00:00:00Z"},
              {"modelId":"gemini-2.5-pro","remainingFraction":0.95},
              {"tokenType":"REQUESTS","remainingFraction":0.5},
              {"modelId":"gemini-2.5-flash-lite"}
            ]}
            """)).ToSnapshot("Gemini Free");

        Assert.Equal("Gemini Free", snap.Plan);
        // Buckets missing modelId or remainingFraction are dropped.
        Assert.Equal(2, snap.Quotas.Count);
        var flash = snap.Quotas[0];
        Assert.Equal("gemini-2.5-flash", flash.Model);
        Assert.Equal(1, flash.UtilizationPct); // 1 - 0.99 → 1%
        Assert.Equal(990, flash.Remaining);
        Assert.NotNull(flash.ResetsAt);
        Assert.Equal(5, snap.Quotas[1].UtilizationPct); // 1 - 0.95 → 5%
        Assert.Null(snap.Quotas[1].ResetsAt);
    }

    [Fact]
    public void GeminiEmptyResponseYieldsNoQuotas()
    {
        var snap = GeminiQuotaResponse.Parse(B("{}")).ToSnapshot("Gemini");
        Assert.Empty(snap.Quotas);
    }

    [Fact]
    public void GeminiPlanLabelFromTier()
    {
        Assert.Equal("Gemini Standard", Plan("""{"currentTier":{"id":"standard-tier"}}"""));
        Assert.Equal("Gemini Free", Plan("""{"currentTier":{"id":"free-tier"}}"""));
        Assert.Equal("Gemini Pro Plan", Plan("""{"currentTier":{"id":"x","name":"Pro Plan"}}"""));
        // A name that already carries the brand isn't prefixed again.
        Assert.Equal("Gemini Code Assist", Plan("""{"currentTier":{"name":"Gemini Code Assist"}}"""));
        Assert.Equal("Gemini", Plan("{}"));
    }

    private static string Plan(string json) => GeminiLoadResponse.Parse(B(json)).PlanLabel();

    [Fact]
    public void GeminiGroupsByVariantWithHighestPct()
    {
        var snap = GeminiQuotaResponse.Parse(B("""
            {"buckets":[
              {"modelId":"gemini-2.5-flash","remainingFraction":0.99},
              {"modelId":"gemini-3-flash-preview","remainingFraction":0.90},
              {"modelId":"gemini-2.5-flash-lite","remainingFraction":0.99},
              {"modelId":"gemini-2.5-pro","remainingFraction":0.95},
              {"modelId":"gemini-3.1-pro-preview","remainingFraction":0.80}
            ]}
            """)).ToSnapshot("Gemini Free");

        var grouped = snap.GroupedByVariant();
        // Three tiers, each the highest usage in its variant, ordered desc.
        Assert.Equal(3, grouped.Count);
        Assert.Equal("Pro", grouped[0].Model);
        Assert.Equal(20, grouped[0].UtilizationPct);        // 1 − 0.80
        Assert.Equal("Flash", grouped[1].Model);
        Assert.Equal(10, grouped[1].UtilizationPct);        // 1 − 0.90 (beats 0.99)
        Assert.Equal("Flash Lite", grouped[2].Model);
        Assert.Equal(1, grouped[2].UtilizationPct);
    }

    [Fact]
    public void GeminiVariantOfMapsTiers()
    {
        Assert.Equal("Flash Lite", Core.Models.GeminiSnapshot.VariantOf("gemini-3.1-flash-lite-preview"));
        Assert.Equal("Flash", Core.Models.GeminiSnapshot.VariantOf("gemini-2.5-flash"));
        Assert.Equal("Pro", Core.Models.GeminiSnapshot.VariantOf("gemini-3.1-pro-preview"));
        Assert.Equal("imagen-3", Core.Models.GeminiSnapshot.VariantOf("imagen-3")); // unknown tier → raw id
    }

    [Fact]
    public void GeminiCacheRoundTrips()
    {
        var snap = new Core.Models.GeminiSnapshot("Gemini Free",
            [new Core.Models.GeminiQuota("gemini-2.5-flash", 12, DateTimeOffset.Parse("2026-06-03T00:00:00Z"), 880)]);
        var back = GeminiCacheDto.Deserialize(GeminiCacheDto.Serialize(snap));
        Assert.Equal(snap.Plan, back.Plan);
        Assert.Equal(snap.Quotas, back.Quotas); // GeminiQuota is a scalar record → element-wise equality
    }
}

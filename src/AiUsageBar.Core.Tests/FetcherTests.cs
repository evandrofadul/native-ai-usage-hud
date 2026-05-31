using System.Net;
using System.Text;
using AiUsageBar.Core.Caching;
using AiUsageBar.Core.Errors;
using AiUsageBar.Core.Tests.Support;
using AiUsageBar.Core.Vendors.Anthropic;
using AiUsageBar.Core.Vendors.Copilot;
using AiUsageBar.Core.Vendors.OpenRouter;
using AiUsageBar.Core.Vendors.Zai;
using Xunit;

namespace AiUsageBar.Core.Tests;

public class FetcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-ub-fetch-" + Guid.NewGuid().ToString("N"));

    private Cache NewCache(string vendor)
    {
        var c = new Cache(Path.Combine(_root, vendor));
        c.EnsureDir();
        return c;
    }

    private string WriteAnthropicCreds()
    {
        // Expires 1h in the future → no refresh needed.
        var expiresMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;
        var path = Path.Combine(_root, ".credentials.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path,
            "{\"claudeAiOauth\":{\"accessToken\":\"AT\",\"refreshToken\":\"RT\",\"expiresAt\":" +
            expiresMs +
            ",\"subscriptionType\":\"max\",\"rateLimitTier\":\"default_claude_max_5x\"}}");
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task AnthropicFreshCacheSkipsNetwork()
    {
        var cache = NewCache("anthropic");
        cache.WritePayload(Encoding.UTF8.GetBytes("""
            {"five_hour":{"utilization":42,"resets_at":"2026-05-23T17:30:00Z"},
             "seven_day":{"utilization":15,"resets_at":"2026-05-30T12:00:00Z"}}
            """));
        var handler = new FakeHttpHandler(); // no routes → would 404 if called
        var fetcher = new AnthropicFetcher(handler.Client(), WriteAnthropicCreds(), cache,
            TimeSpan.FromSeconds(60), "https://fake.local/api/oauth/usage", "https://fake.local/v1/oauth/token");

        var outcome = await fetcher.FetchAsync();
        var snap = (Core.Models.AnthropicSnapshot)outcome.Snapshot;
        Assert.Equal(42, snap.Session.UtilizationPct);
        Assert.False(outcome.Stale);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnthropicLiveFetchWritesCacheAndReturnsSnapshot()
    {
        var cache = NewCache("anthropic");
        var handler = new FakeHttpHandler().On("/api/oauth/usage", HttpStatusCode.OK, """
            {"five_hour":{"utilization":50,"resets_at":"2026-05-23T17:30:00Z"},
             "seven_day":{"utilization":25,"resets_at":"2026-05-30T12:00:00Z"}}
            """);
        var fetcher = new AnthropicFetcher(handler.Client(), WriteAnthropicCreds(), cache,
            TimeSpan.Zero, "https://fake.local/api/oauth/usage", "https://fake.local/v1/oauth/token");

        var outcome = await fetcher.FetchAsync();
        var snap = (Core.Models.AnthropicSnapshot)outcome.Snapshot;
        Assert.Equal(50, snap.Session.UtilizationPct);
        Assert.False(outcome.Stale);
        Assert.NotNull(cache.MaybePayload());
    }

    [Fact]
    public async Task Anthropic429FallsBackToStaleCache()
    {
        var cache = NewCache("anthropic");
        cache.WritePayload(Encoding.UTF8.GetBytes("""
            {"five_hour":{"utilization":12,"resets_at":"2026-05-23T17:30:00Z"},
             "seven_day":{"utilization":5,"resets_at":"2026-05-30T12:00:00Z"}}
            """));
        var handler = new FakeHttpHandler().On("/api/oauth/usage", HttpStatusCode.TooManyRequests,
            """{"error":{"type":"rate_limit_error","message":"slow down"}}""");
        var fetcher = new AnthropicFetcher(handler.Client(), WriteAnthropicCreds(), cache,
            TimeSpan.Zero, "https://fake.local/api/oauth/usage", "https://fake.local/v1/oauth/token");

        var outcome = await fetcher.FetchAsync();
        var snap = (Core.Models.AnthropicSnapshot)outcome.Snapshot;
        Assert.True(outcome.Stale);
        Assert.Equal(12, snap.Session.UtilizationPct);
        Assert.Equal(429, outcome.LastError!.Value.Code);
        Assert.Equal("slow down", outcome.LastError.Value.Message);
    }

    [Fact]
    public async Task ZaiLiveFetchAndStaleFallback()
    {
        var cache = NewCache("zai");
        var handler = new FakeHttpHandler().On("/quota/limit", HttpStatusCode.OK,
            """{"code":200,"data":{"limits":[{"type":"TOKENS_LIMIT","percentage":10}],"level":"pro"},"success":true}""");
        var fetcher = new ZaiFetcher(handler.Client(), "KEY", cache, TimeSpan.Zero, null,
            "https://fake.local/api/monitor/usage/quota/limit");
        var outcome = await fetcher.FetchAsync();
        var snap = (Core.Models.ZaiSnapshot)outcome.Snapshot;
        Assert.Equal(10, snap.Session!.Value.UtilizationPct);
        Assert.False(outcome.Stale);

        // Now a 500 → falls back to the just-written cache, marked stale.
        var bad = new FakeHttpHandler().On("/quota/limit", HttpStatusCode.InternalServerError, "boom");
        var fetcher2 = new ZaiFetcher(bad.Client(), "KEY", cache, TimeSpan.Zero, null,
            "https://fake.local/api/monitor/usage/quota/limit");
        var outcome2 = await fetcher2.FetchAsync();
        Assert.True(outcome2.Stale);
        Assert.Equal(500, outcome2.LastError!.Value.Code);
    }

    [Fact]
    public async Task ZaiSendsBareAuthorizationHeader()
    {
        var cache = NewCache("zai");
        var handler = new FakeHttpHandler().On("/quota/limit", HttpStatusCode.OK,
            """{"code":200,"data":{"limits":[],"level":"pro"},"success":true}""");
        var fetcher = new ZaiFetcher(handler.Client(), "MYKEY", cache, TimeSpan.Zero, null,
            "https://fake.local/api/monitor/usage/quota/limit");
        await fetcher.FetchAsync();
        var auth = handler.Requests[0].Headers.GetValues("Authorization").Single();
        Assert.Equal("MYKEY", auth); // no "Bearer " prefix
    }

    [Fact]
    public async Task OpenRouterCombinesAndCaches()
    {
        var cache = NewCache("openrouter");
        var handler = new FakeHttpHandler()
            .On("/credits", HttpStatusCode.OK, """{"data":{"total_credits":100.0,"total_usage":30.0}}""")
            .On("/key", HttpStatusCode.OK, """{"data":{"label":"k","usage_daily":1,"usage_weekly":5,"usage_monthly":30,"is_free_tier":false}}""");
        var fetcher = new OpenRouterFetcher(handler.Client(), "KEY", cache, TimeSpan.Zero,
            "https://fake.local/api/v1/credits", "https://fake.local/api/v1/key");
        var outcome = await fetcher.FetchAsync();
        var snap = (Core.Models.OpenRouterSnapshot)outcome.Snapshot;
        Assert.Equal(70.0, snap.Balance(), 9);
        Assert.Equal("OpenRouter — k", snap.Label);
        Assert.NotNull(cache.MaybePayload());
    }

    [Fact]
    public async Task CopilotLiveFetchAndStaleFallback()
    {
        var cache = NewCache("copilot");
        var handler = new FakeHttpHandler().On("/copilot_internal/user", HttpStatusCode.OK, """
            {"copilot_plan":"individual","chat_enabled":true,"quota_reset_date":"2026-06-01",
             "quota_snapshots":{"premium_interactions":{"entitlement":300,"remaining":150,"percent_remaining":50,"unlimited":false}}}
            """);
        var fetcher = new CopilotFetcher(handler.Client(), () => "gho_test", cache, TimeSpan.Zero,
            "https://fake.local/copilot_internal/user");

        var outcome = await fetcher.FetchAsync();
        var snap = (Core.Models.CopilotSnapshot)outcome.Snapshot;
        Assert.Equal("Copilot Individual", snap.Plan);
        Assert.Equal(50, snap.Quotas[0].UtilizationPct);
        Assert.False(outcome.Stale);

        // A 500 → falls back to the cached snapshot, marked stale.
        var bad = new FakeHttpHandler().On("/copilot_internal/user", HttpStatusCode.InternalServerError, "boom");
        var fetcher2 = new CopilotFetcher(bad.Client(), () => "gho_test", cache, TimeSpan.Zero,
            "https://fake.local/copilot_internal/user");
        var outcome2 = await fetcher2.FetchAsync();
        Assert.True(outcome2.Stale);
        Assert.Equal(500, outcome2.LastError!.Value.Code);
    }

    [Fact]
    public async Task CopilotSendsTokenAuthorizationHeader()
    {
        var cache = NewCache("copilot");
        var handler = new FakeHttpHandler().On("/copilot_internal/user", HttpStatusCode.OK,
            """{"copilot_plan":"free","quota_snapshots":{}}""");
        var fetcher = new CopilotFetcher(handler.Client(), () => "gho_MYTOKEN", cache, TimeSpan.Zero,
            "https://fake.local/copilot_internal/user");
        await fetcher.FetchAsync();
        var auth = handler.Requests[0].Headers.GetValues("Authorization").Single();
        Assert.Equal("token gho_MYTOKEN", auth);
    }

    [Fact]
    public async Task TransientErrorWithNoCacheThrows()
    {
        var cache = NewCache("zai");
        // Point at a real handler that throws a transport error by using a closed scheme.
        var handler = new ThrowingHandler();
        var fetcher = new ZaiFetcher(new HttpClient(handler), "KEY", cache, TimeSpan.Zero, null,
            "https://fake.local/api/monitor/usage/quota/limit");
        await Assert.ThrowsAsync<TransportException>(() => fetcher.FetchAsync());
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }
}

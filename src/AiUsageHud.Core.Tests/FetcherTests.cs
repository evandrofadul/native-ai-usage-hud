using System.Net;
using System.Text;
using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Tests.Support;
using AiUsageHud.Core.Vendors.Anthropic;
using AiUsageHud.Core.Vendors.Copilot;
using AiUsageHud.Core.Vendors.Gemini;

namespace AiUsageHud.Core.Tests;

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

    private string WriteGeminiCreds()
    {
        // Expires 1h in the future → no refresh needed.
        var expiresMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;
        var path = Path.Combine(_root, "gemini_oauth.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path,
            "{\"access_token\":\"AT\",\"refresh_token\":\"RT\",\"expiry_date\":" + expiresMs + "}");
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
    public async Task GeminiLiveFetchAndStaleFallback()
    {
        var cache = NewCache("gemini");
        var handler = new FakeHttpHandler()
            .On("/v1internal:loadCodeAssist", HttpStatusCode.OK,
                """{"cloudaicompanionProject":"proj-123","currentTier":{"id":"free-tier","name":"Free"}}""")
            .On("/v1internal:retrieveUserQuota", HttpStatusCode.OK,
                """{"buckets":[{"modelId":"gemini-2.5-flash","remainingFraction":0.95,"resetTime":"2026-06-03T00:00:00Z"}]}""");
        var fetcher = new GeminiFetcher(handler.Client(), WriteGeminiCreds(), cache, TimeSpan.Zero, null,
            "https://fake.local");

        var outcome = await fetcher.FetchAsync();
        var snap = (Core.Models.GeminiSnapshot)outcome.Snapshot;
        Assert.Equal("Gemini Free", snap.Plan);
        Assert.Equal(5, snap.Quotas[0].UtilizationPct); // 1 - 0.95 → 5%
        Assert.False(outcome.Stale);
        // Bearer auth on both calls.
        Assert.All(handler.Requests, r =>
            Assert.Equal("Bearer AT", r.Headers.GetValues("Authorization").Single()));

        // A 500 on the quota call → falls back to the cached snapshot, marked stale.
        var bad = new FakeHttpHandler()
            .On("/v1internal:loadCodeAssist", HttpStatusCode.OK, """{"currentTier":{"id":"free-tier"}}""")
            .On("/v1internal:retrieveUserQuota", HttpStatusCode.InternalServerError, "boom");
        var fetcher2 = new GeminiFetcher(bad.Client(), WriteGeminiCreds(), cache, TimeSpan.Zero, null,
            "https://fake.local");
        var outcome2 = await fetcher2.FetchAsync();
        Assert.True(outcome2.Stale);
        Assert.Equal(500, outcome2.LastError!.Value.Code);
    }

    [Fact]
    public async Task GeminiFreshCacheSkipsNetwork()
    {
        var cache = NewCache("gemini");
        cache.WritePayload(GeminiCacheDto.Serialize(new Core.Models.GeminiSnapshot("Gemini Free",
            [new Core.Models.GeminiQuota("gemini-2.5-flash", 7, null, null)])));
        var handler = new FakeHttpHandler(); // no routes → would 404 if called
        var fetcher = new GeminiFetcher(handler.Client(), WriteGeminiCreds(), cache,
            TimeSpan.FromSeconds(60), null, "https://fake.local");

        var outcome = await fetcher.FetchAsync();
        var snap = (Core.Models.GeminiSnapshot)outcome.Snapshot;
        Assert.Equal(7, snap.Quotas[0].UtilizationPct);
        Assert.False(outcome.Stale);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TransientErrorWithNoCacheThrows()
    {
        var cache = NewCache("anthropic");
        // A transport error with no cache to fall back on must surface as TransportException.
        var handler = new ThrowingHandler();
        var fetcher = new AnthropicFetcher(new HttpClient(handler), WriteAnthropicCreds(), cache, TimeSpan.Zero,
            "https://fake.local/api/oauth/usage", "https://fake.local/v1/oauth/token");
        await Assert.ThrowsAsync<TransportException>(() => fetcher.FetchAsync());
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }
}

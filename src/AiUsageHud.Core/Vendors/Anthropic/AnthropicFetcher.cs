using AiUsageHud.Core.Abstractions;
using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Vendors.Anthropic;

/// <summary>
/// Read creds → maybe-refresh → GET usage → cache result, falling back to the
/// on-disk cache on failure. Port of the Rust <c>anthropic/fetch.rs</c>.
/// </summary>
public sealed class AnthropicFetcher : IVendorFetcher
{
    public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    public const string UsageBetaHeader = "oauth-2025-04-20";

    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(45);

    private readonly HttpClient _client;
    private readonly string _credsPath;
    private readonly Cache _cache;
    private readonly TimeSpan _ttl;
    private readonly string _usageUrl;
    private readonly string _tokenUrl;

    public AnthropicFetcher(HttpClient client, string credsPath, Cache cache, TimeSpan ttl,
        string? usageUrl = null, string? tokenUrl = null)
    {
        _client = client;
        _credsPath = credsPath;
        _cache = cache;
        _ttl = ttl;
        _usageUrl = usageUrl ?? UsageUrl;
        _tokenUrl = tokenUrl ?? AnthropicOAuth.TokenUrl;
    }

    public VendorId Id => VendorId.Anthropic;

    public async Task<VendorOutcome> FetchAsync(CancellationToken ct = default)
    {
        _cache.EnsureDir();
        using var _lock = _cache.AcquireLock(LockTimeout);

        var creds = AnthropicCreds.ReadFrom(_credsPath);
        var planLabel = creds.PlanLabel();

        // Fast path: fresh cache — but a corrupt payload falls through to a live
        // fetch rather than being rendered as a zeroed "Unknown" snapshot for the
        // rest of the TTL.
        var fresh = _cache.FreshPayload(_ttl);
        if (fresh is not null && TryParseSnapshot(fresh, planLabel, out var freshSnap))
            return new VendorOutcome(freshSnap, false, _cache.ReadLastError(), _cache.PayloadAge());

        // Maybe refresh.
        var nowSecs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var authOk = true;
        var refreshTransient = false;
        if (AnthropicOAuth.NeedsRefresh(creds.ExpiresAtSecs, nowSecs))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(RefreshTimeout);
                var rr = await AnthropicOAuth.RefreshAsync(_client, _tokenUrl, creds.RefreshToken, cts.Token);
                creds.AccessToken = rr.AccessToken;
                if (!string.IsNullOrEmpty(rr.RefreshToken)) creds.RefreshToken = rr.RefreshToken!;
                creds.ExpiresAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + rr.ExpiresIn * 1000;
                try { AnthropicCreds.WriteBack(_credsPath, creds); } catch { /* best effort */ }
            }
            catch (HttpStatusException e) { authOk = false; _cache.WriteLastError(e.Status, e.Body); }
            catch (AppException e) when (e.IsTransient) { authOk = false; refreshTransient = true; }
            catch (OperationCanceledException) { authOk = false; refreshTransient = true; }
            catch (AppException e) { authOk = false; _cache.WriteLastError(0, e.Message); }
        }

        if (!authOk) return HandleAuthFailure(planLabel, refreshTransient);

        // Fetch usage.
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HttpTimeout);
            var bytes = await FetchUsage(creds, cts.Token);
            _cache.WritePayload(bytes);
            var snap = AnthropicUsageResponse.Parse(bytes).ToSnapshot(planLabel);
            return new VendorOutcome(snap, false, null, TimeSpan.Zero);
        }
        catch (HttpStatusException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(e.Status, e.Body);
            return FallbackToCache(planLabel, (e.Status, e.Body));
        }
        catch (AppException e) when (e.IsTransient)
        {
            return FallbackToCacheSilent(planLabel);
        }
        catch (OperationCanceledException)
        {
            return FallbackToCacheSilent(planLabel);
        }
        catch (AppException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(0, e.Message);
            return FallbackToCache(planLabel, (0, e.Message));
        }
    }

    private async Task<byte[]> FetchUsage(OauthCreds creds, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _usageUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {creds.AccessToken}");
        req.Headers.TryAddWithoutValidation("anthropic-beta", UsageBetaHeader);

        HttpResponseMessage resp;
        try { resp = await _client.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new TransportException(e.Message); }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        if (resp.IsSuccessStatusCode)
        {
            // Validate it parses as a usage shape.
            try { AnthropicUsageResponse.Parse(bytes); }
            catch (Exception e) { throw new SchemaException($"usage response unparseable: {e.Message}"); }
            return bytes;
        }

        var body = System.Text.Encoding.UTF8.GetString(bytes);
        var msg = AnthropicOAuth.ParseErrorBody(body) ?? new string(body.Take(200).ToArray());
        throw new HttpStatusException((int)resp.StatusCode, msg);
    }

    private static bool TryParseSnapshot(byte[] bytes, string planLabel, out AnthropicSnapshot snap)
    {
        try { snap = AnthropicUsageResponse.Parse(bytes).ToSnapshot(planLabel); return true; }
        catch { snap = null!; return false; }
    }

    private VendorOutcome FallbackToCache(string planLabel, (int, string) lastError)
    {
        var bytes = _cache.FallbackPayload(Cache.MaxStale) ?? throw new OtherException("no usable cache");
        var snap = AnthropicUsageResponse.Parse(bytes).ToSnapshot(planLabel);
        return new VendorOutcome(snap, true, lastError, _cache.PayloadAge());
    }

    private VendorOutcome FallbackToCacheSilent(string planLabel)
    {
        var bytes = _cache.FallbackPayload(Cache.MaxStale)
            ?? throw new TransportException("no cache and network unreachable");
        var snap = AnthropicUsageResponse.Parse(bytes).ToSnapshot(planLabel);
        return new VendorOutcome(snap, true, _cache.ReadLastError(), _cache.PayloadAge());
    }

    private VendorOutcome HandleAuthFailure(string planLabel, bool transient)
    {
        var bytes = _cache.FallbackPayload(Cache.MaxStale);
        if (bytes is null)
        {
            throw transient
                ? new TransportException("no cache and refresh failed transiently")
                : new CredentialsException("token refresh failed; run `claude` to re-auth");
        }
        var snap = AnthropicUsageResponse.Parse(bytes).ToSnapshot(planLabel);
        return new VendorOutcome(snap, true, _cache.ReadLastError(), _cache.PayloadAge());
    }
}

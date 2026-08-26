using AiUsageHud.Core.Abstractions;
using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Http;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Vendors.OpenAi;

/// <summary>
/// Read ~/.codex/auth.json → maybe refresh → fetch usage → cache. Port of the
/// Rust <c>openai/fetch.rs</c>.
/// </summary>
public sealed class OpenAiFetcher : IVendorFetcher
{
    public const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";

    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(45);

    private readonly HttpClient _client;
    private readonly string _credsPath;
    private readonly Cache _cache;
    private readonly TimeSpan _ttl;
    private readonly string _usageUrl;
    private readonly string _tokenUrl;

    public OpenAiFetcher(HttpClient client, string credsPath, Cache cache, TimeSpan ttl,
        string? usageUrl = null, string? tokenUrl = null)
    {
        _client = client;
        _credsPath = credsPath;
        _cache = cache;
        _ttl = ttl;
        _usageUrl = usageUrl ?? UsageUrl;
        _tokenUrl = tokenUrl ?? OpenAiOAuth.TokenUrl;
    }

    public VendorId Id => VendorId.Openai;

    public async Task<VendorOutcome> FetchAsync(CancellationToken ct = default)
    {
        _cache.EnsureDir();
        using var _lock = _cache.AcquireLock(LockTimeout);

        var auth = OpenAiCreds.ReadFrom(_credsPath);
        var planHint = auth.Tokens.PlanTypeFromIdToken();

        // A corrupt fresh payload falls through to a live fetch rather than
        // being rendered as a zeroed snapshot for the rest of the TTL.
        var fresh = _cache.FreshPayload(_ttl);
        if (fresh is not null && TryParseSnapshot(fresh, planHint, out var freshSnap))
            return new VendorOutcome(freshSnap, false, _cache.ReadLastError(), _cache.PayloadAge());

        var nowSecs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (OpenAiOAuth.NeedsRefresh(auth.Tokens.ExpiresAtSecs, nowSecs))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(RefreshTimeout);
                var rr = await OpenAiOAuth.RefreshAsync(_client, _tokenUrl, auth.Tokens.RefreshToken, cts.Token);
                auth.Tokens.AccessToken = rr.AccessToken;
                if (!string.IsNullOrEmpty(rr.RefreshToken)) auth.Tokens.RefreshToken = rr.RefreshToken!;
                if (!string.IsNullOrEmpty(rr.IdToken)) auth.Tokens.IdToken = rr.IdToken!;
                try { OpenAiCreds.WriteBack(_credsPath, auth); } catch { /* best effort */ }
            }
            catch (HttpStatusException e) { _cache.WriteLastError(e.Status, e.Body); return HandleAuthFailure(planHint, false); }
            catch (AppException e) when (e.IsTransient) { return HandleAuthFailure(planHint, true); }
            catch (OperationCanceledException) { return HandleAuthFailure(planHint, true); }
            catch (AppException e) { _cache.WriteLastError(0, e.Message); return HandleAuthFailure(planHint, false); }
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HttpTimeout);
            var bytes = await FetchUsage(auth.Tokens, cts.Token);
            _cache.WritePayload(bytes);
            var snap = OpenAiUsageResponse.Parse(bytes).ToSnapshot(planHint);
            return new VendorOutcome(snap, false, null, TimeSpan.Zero);
        }
        catch (HttpStatusException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(e.Status, e.Body);
            return Fallback(planHint, (e.Status, e.Body));
        }
        catch (AppException e) when (e.IsTransient) { return FallbackSilent(planHint); }
        catch (OperationCanceledException) { return FallbackSilent(planHint); }
        catch (AppException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(0, e.Message);
            return Fallback(planHint, (0, e.Message));
        }
    }

    private async Task<byte[]> FetchUsage(Tokens t, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _usageUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {t.AccessToken}");
        req.Headers.TryAddWithoutValidation("User-Agent", "codex-cli");
        if (!string.IsNullOrEmpty(t.AccountId))
            req.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", t.AccountId);

        HttpResponseMessage resp;
        try { resp = await _client.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new TransportException(e.Message); }

        var bytes = await CappedBody.ReadAsync(resp.Content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = new string(System.Text.Encoding.UTF8.GetString(bytes).Take(200).ToArray());
            throw new HttpStatusException((int)resp.StatusCode, body);
        }
        try { OpenAiUsageResponse.Parse(bytes); }
        catch (Exception e) { throw new SchemaException($"openai usage response: {e.Message}"); }
        return bytes;
    }

    private static bool TryParseSnapshot(byte[] bytes, string? planHint, out OpenAiSnapshot snap)
    {
        try { snap = OpenAiUsageResponse.Parse(bytes).ToSnapshot(planHint); return true; }
        catch { snap = null!; return false; }
    }

    private VendorOutcome Reuse(byte[] bytes, bool stale, string? planHint)
    {
        var snap = OpenAiUsageResponse.Parse(bytes).ToSnapshot(planHint);
        return new VendorOutcome(snap, stale, _cache.ReadLastError(), _cache.PayloadAge());
    }

    private VendorOutcome Fallback(string? planHint, (int, string) lastError)
    {
        var bytes = _cache.FallbackPayload(Cache.MaxStale) ?? throw new OtherException("openai: no usable cache");
        var outcome = Reuse(bytes, true, planHint);
        return outcome with { LastError = lastError };
    }

    private VendorOutcome FallbackSilent(string? planHint)
    {
        var bytes = _cache.FallbackPayload(Cache.MaxStale)
            ?? throw new TransportException("openai: no cache and network unreachable");
        return Reuse(bytes, true, planHint);
    }

    private VendorOutcome HandleAuthFailure(string? planHint, bool transient)
    {
        var bytes = _cache.FallbackPayload(Cache.MaxStale);
        if (bytes is null)
        {
            throw transient
                ? new TransportException("openai: no cache and refresh failed transiently")
                : new CredentialsException("openai: token refresh failed; run `codex login` to re-auth");
        }
        return Reuse(bytes, true, planHint);
    }
}

using AiUsageBar.Core.Abstractions;
using AiUsageBar.Core.Caching;
using AiUsageBar.Core.Errors;
using AiUsageBar.Core.Models;

namespace AiUsageBar.Core.Vendors.OpenRouter;

/// <summary>
/// OpenRouter fetch — combines <c>/api/v1/credits</c> and <c>/api/v1/key</c>.
/// Port of the Rust <c>openrouter/fetch.rs</c>.
/// </summary>
public sealed class OpenRouterFetcher : IVendorFetcher
{
    public const string BaseUrl = "https://openrouter.ai/api/v1";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _client;
    private readonly string _apiKey;
    private readonly Cache _cache;
    private readonly TimeSpan _ttl;
    private readonly string _creditsUrl;
    private readonly string _keyUrl;

    public OpenRouterFetcher(HttpClient client, string apiKey, Cache cache, TimeSpan ttl,
        string? creditsUrl = null, string? keyUrl = null)
    {
        _client = client;
        _apiKey = apiKey;
        _cache = cache;
        _ttl = ttl;
        _creditsUrl = creditsUrl ?? $"{BaseUrl}/credits";
        _keyUrl = keyUrl ?? $"{BaseUrl}/key";
    }

    public VendorId Id => VendorId.Openrouter;

    public async Task<VendorOutcome> FetchAsync(CancellationToken ct = default)
    {
        _cache.EnsureDir();
        using var _lock = _cache.AcquireLock(LockTimeout);

        var fresh = _cache.FreshPayload(_ttl);
        if (fresh is not null) return Reuse(fresh, stale: false);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HttpTimeout);
            var creditsJson = await Get(_creditsUrl, cts.Token);
            var keyJson = await Get(_keyUrl, cts.Token);
            var snap = OpenRouterMapping.Combine(
                OpenRouterMapping.ParseCredits(creditsJson),
                OpenRouterMapping.ParseKey(keyJson));
            _cache.WritePayload(OpenRouterCacheDto.Serialize(snap));
            return new VendorOutcome(snap, false, null, TimeSpan.Zero);
        }
        catch (AppException e) when (e.IsTransient) { return FallbackSilent(); }
        catch (OperationCanceledException) { return FallbackSilent(); }
        catch (HttpStatusException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(e.Status, e.Body);
            return FallbackWithError((e.Status, e.Body));
        }
        catch (AppException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(0, e.Message);
            return FallbackWithError((0, e.Message));
        }
    }

    private async Task<string> Get(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");

        HttpResponseMessage resp;
        try { resp = await _client.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new TransportException(e.Message); }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpStatusException((int)resp.StatusCode, new string(body.Take(200).ToArray()));
        return body;
    }

    private VendorOutcome Reuse(byte[] bytes, bool stale)
    {
        var snap = OpenRouterCacheDto.Deserialize(bytes);
        return new VendorOutcome(snap, stale, _cache.ReadLastError(), _cache.PayloadAge());
    }

    private VendorOutcome FallbackSilent()
    {
        var bytes = _cache.MaybePayload()
            ?? throw new TransportException("openrouter: no cache and network unreachable");
        return Reuse(bytes, true);
    }

    private VendorOutcome FallbackWithError((int, string) lastError)
    {
        var bytes = _cache.MaybePayload() ?? throw new OtherException("openrouter: no usable cache");
        var outcome = Reuse(bytes, true);
        return outcome with { LastError = lastError };
    }
}

using AiUsageBar.Core.Abstractions;
using AiUsageBar.Core.Caching;
using AiUsageBar.Core.Errors;
using AiUsageBar.Core.Models;

namespace AiUsageBar.Core.Vendors.Zai;

/// <summary>
/// Z.AI fetch. The API key is passed as <c>Authorization: &lt;KEY&gt;</c> WITHOUT
/// the <c>Bearer</c> prefix (sending Bearer returns 401). Port of <c>zai/fetch.rs</c>.
/// </summary>
public sealed class ZaiFetcher : IVendorFetcher
{
    public const string QuotaUrl = "https://api.z.ai/api/monitor/usage/quota/limit";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _client;
    private readonly string _apiKey;
    private readonly Cache _cache;
    private readonly TimeSpan _ttl;
    private readonly string? _planTier;
    private readonly string _quotaUrl;

    public ZaiFetcher(HttpClient client, string apiKey, Cache cache, TimeSpan ttl,
        string? planTier, string? quotaUrl = null)
    {
        _client = client;
        _apiKey = apiKey;
        _cache = cache;
        _ttl = ttl;
        _planTier = planTier;
        _quotaUrl = quotaUrl ?? QuotaUrl;
    }

    public VendorId Id => VendorId.Zai;

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
            var bytes = await FetchLive(cts.Token);
            _cache.WritePayload(bytes);
            var snap = ZaiEnvelope.Parse(bytes).ToSnapshot(_planTier);
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

    private async Task<byte[]> FetchLive(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _quotaUrl);
        req.Headers.TryAddWithoutValidation("Authorization", _apiKey); // no "Bearer"

        HttpResponseMessage resp;
        try { resp = await _client.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new TransportException(e.Message); }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = new string(System.Text.Encoding.UTF8.GetString(bytes).Take(200).ToArray());
            throw new HttpStatusException((int)resp.StatusCode, body);
        }
        return bytes;
    }

    private VendorOutcome Reuse(byte[] bytes, bool stale)
    {
        var snap = ZaiEnvelope.Parse(bytes).ToSnapshot(_planTier);
        return new VendorOutcome(snap, stale, _cache.ReadLastError(), _cache.PayloadAge());
    }

    private VendorOutcome FallbackSilent()
    {
        var bytes = _cache.MaybePayload()
            ?? throw new TransportException("zai: no cache and network unreachable");
        return Reuse(bytes, true);
    }

    private VendorOutcome FallbackWithError((int, string) lastError)
    {
        var bytes = _cache.MaybePayload() ?? throw new OtherException("zai: no usable cache");
        var outcome = Reuse(bytes, true);
        return outcome with { LastError = lastError };
    }
}

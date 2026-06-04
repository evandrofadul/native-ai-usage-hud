using AiUsageHud.Core.Abstractions;
using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Vendors.Copilot;

/// <summary>
/// Resolve the GitHub OAuth token → GET <c>copilot_internal/user</c> → read the plan +
/// quota snapshots → cache, falling back to the on-disk cache on failure.
/// </summary>
public sealed class CopilotFetcher : IVendorFetcher
{
    public const string UsageUrl = "https://api.github.com/copilot_internal/user";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _client;
    private readonly Func<string> _tokenFactory;
    private readonly Cache _cache;
    private readonly TimeSpan _ttl;
    private readonly string _usageUrl;

    public CopilotFetcher(HttpClient client, Func<string> tokenFactory, Cache cache, TimeSpan ttl,
        string? usageUrl = null)
    {
        _client = client;
        _tokenFactory = tokenFactory;
        _cache = cache;
        _ttl = ttl;
        _usageUrl = usageUrl ?? UsageUrl;
    }

    public VendorId Id => VendorId.Copilot;

    public async Task<VendorOutcome> FetchAsync(CancellationToken ct = default)
    {
        _cache.EnsureDir();
        using var _lock = _cache.AcquireLock(LockTimeout);

        var fresh = _cache.FreshPayload(_ttl);
        if (fresh is not null) return Reuse(fresh, stale: false);

        try
        {
            var token = _tokenFactory();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HttpTimeout);
            var json = await Get(token, cts.Token);
            var snap = CopilotUserResponse.Parse(json).ToSnapshot();
            _cache.WritePayload(CopilotCacheDto.Serialize(snap));
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

    private async Task<byte[]> Get(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _usageUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"token {token}");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("User-Agent", "ai-usage-hud");
        req.Headers.TryAddWithoutValidation("Editor-Version", "ai-usage-hud/1.0");

        HttpResponseMessage resp;
        try { resp = await _client.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new TransportException(e.Message); }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = new string(System.Text.Encoding.UTF8.GetString(bytes).Take(200).ToArray());
            throw new HttpStatusException((int)resp.StatusCode, body);
        }
        try { CopilotUserResponse.Parse(bytes); }
        catch (Exception e) { throw new SchemaException($"copilot user response: {e.Message}"); }
        return bytes;
    }

    private VendorOutcome Reuse(byte[] bytes, bool stale)
    {
        var snap = CopilotCacheDto.Deserialize(bytes);
        return new VendorOutcome(snap, stale, _cache.ReadLastError(), _cache.PayloadAge());
    }

    private VendorOutcome FallbackSilent()
    {
        var bytes = _cache.MaybePayload()
            ?? throw new TransportException("copilot: no cache and network unreachable");
        return Reuse(bytes, true);
    }

    private VendorOutcome FallbackWithError((int, string) lastError)
    {
        var bytes = _cache.MaybePayload() ?? throw new OtherException("copilot: no usable cache");
        var outcome = Reuse(bytes, true);
        return outcome with { LastError = lastError };
    }
}

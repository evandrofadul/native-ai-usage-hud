using System.Net.Http.Json;
using AiUsageHud.Core.Abstractions;
using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Json;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Vendors.Gemini;

/// <summary>
/// Read creds → maybe-refresh → resolve the managed project + plan via
/// <c>:loadCodeAssist</c> → GET the per-model buckets via <c>:retrieveUserQuota</c> →
/// cache, falling back to the on-disk cache on failure. Same overall shape as the
/// other OAuth fetchers (<see cref="Anthropic.AnthropicFetcher"/> /
/// <see cref="Copilot.CopilotFetcher"/>), with two POSTs instead of one GET.
/// </summary>
public sealed class GeminiFetcher : IVendorFetcher
{
    public const string Endpoint = "https://cloudcode-pa.googleapis.com";
    public const string ApiVersion = "v1internal";

    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(45);

    private readonly HttpClient _client;
    private readonly string _credsPath;
    private readonly Cache _cache;
    private readonly TimeSpan _ttl;
    private readonly string? _projectOverride;
    private readonly string _endpoint;
    private readonly string _tokenUrl;

    public GeminiFetcher(HttpClient client, string credsPath, Cache cache, TimeSpan ttl,
        string? projectId = null, string? endpoint = null, string? tokenUrl = null)
    {
        _client = client;
        _credsPath = credsPath;
        _cache = cache;
        _ttl = ttl;
        _projectOverride = string.IsNullOrWhiteSpace(projectId) ? null : projectId;
        _endpoint = (endpoint ?? Endpoint).TrimEnd('/');
        _tokenUrl = tokenUrl ?? GeminiOAuth.TokenUrl;
    }

    public VendorId Id => VendorId.Gemini;

    private string MethodUrl(string method) => $"{_endpoint}/{ApiVersion}:{method}";

    public async Task<VendorOutcome> FetchAsync(CancellationToken ct = default)
    {
        _cache.EnsureDir();
        using var _lock = _cache.AcquireLock(LockTimeout);

        var creds = GeminiCreds.ReadFrom(_credsPath);

        // Fast path: fresh cache (the snapshot carries its own plan label).
        var fresh = _cache.FreshPayload(_ttl);
        if (fresh is not null) return ReuseCache(fresh, stale: false);

        // Maybe refresh the access token.
        var nowSecs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var authOk = true;
        var refreshTransient = false;
        if (GeminiOAuth.NeedsRefresh(creds.ExpiresAtSecs, nowSecs))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(RefreshTimeout);
                var rr = await GeminiOAuth.RefreshAsync(_client, _tokenUrl, creds.RefreshToken, cts.Token);
                creds.AccessToken = rr.AccessToken;
                creds.ExpiryDateMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + rr.ExpiresIn * 1000;
                try { GeminiCreds.WriteBack(_credsPath, creds); } catch { /* best effort */ }
            }
            catch (HttpStatusException e) { authOk = false; _cache.WriteLastError(e.Status, e.Body); }
            catch (AppException e) when (e.IsTransient) { authOk = false; refreshTransient = true; }
            catch (OperationCanceledException) { authOk = false; refreshTransient = true; }
            catch (AppException e) { authOk = false; _cache.WriteLastError(0, e.Message); }
        }

        if (!authOk) return HandleAuthFailure(refreshTransient);

        // Resolve project + tier, then fetch the quota buckets.
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HttpTimeout);

            var load = GeminiLoadResponse.Parse(await LoadCodeAssist(creds, cts.Token));
            var project = _projectOverride ?? load.CloudaicompanionProject ?? "";
            var bytes = await RetrieveQuota(creds, project, cts.Token);

            var snap = GeminiQuotaResponse.Parse(bytes).ToSnapshot(load.PlanLabel());
            _cache.WritePayload(GeminiCacheDto.Serialize(snap));
            return new VendorOutcome(snap, false, null, TimeSpan.Zero);
        }
        catch (HttpStatusException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(e.Status, e.Body);
            return FallbackToCache((e.Status, e.Body));
        }
        catch (AppException e) when (e.IsTransient) { return FallbackToCacheSilent(); }
        catch (OperationCanceledException) { return FallbackToCacheSilent(); }
        catch (AppException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(0, e.Message);
            return FallbackToCache((0, e.Message));
        }
    }

    private async Task<byte[]> LoadCodeAssist(GeminiOauthCreds creds, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, MethodUrl("loadCodeAssist"))
        {
            Content = JsonContent.Create(
                new GeminiLoadRequest { CloudaicompanionProject = _projectOverride },
                AppJsonContext.Default.GeminiLoadRequest),
        };
        return await Send(req, creds, "loadCodeAssist", ct);
    }

    private async Task<byte[]> RetrieveQuota(GeminiOauthCreds creds, string project, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, MethodUrl("retrieveUserQuota"))
        {
            Content = JsonContent.Create(
                new GeminiQuotaRequest { Project = project },
                AppJsonContext.Default.GeminiQuotaRequest),
        };
        return await Send(req, creds, "retrieveUserQuota", ct);
    }

    private async Task<byte[]> Send(HttpRequestMessage req, GeminiOauthCreds creds, string what, CancellationToken ct)
    {
        using var _ = req;
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {creds.AccessToken}");

        HttpResponseMessage resp;
        try { resp = await _client.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new TransportException(e.Message); }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        if (resp.IsSuccessStatusCode) return bytes;

        var body = Anthropic.AnthropicOAuth.ParseErrorBody(System.Text.Encoding.UTF8.GetString(bytes))
            ?? new string(System.Text.Encoding.UTF8.GetString(bytes).Take(200).ToArray());
        throw new HttpStatusException((int)resp.StatusCode, $"{what}: {body}");
    }

    private VendorOutcome ReuseCache(byte[] bytes, bool stale)
    {
        var snap = GeminiCacheDto.Deserialize(bytes);
        return new VendorOutcome(snap, stale, _cache.ReadLastError(), _cache.PayloadAge());
    }

    private VendorOutcome FallbackToCache((int, string) lastError)
    {
        var bytes = _cache.MaybePayload() ?? throw new OtherException("gemini: no usable cache");
        var outcome = ReuseCache(bytes, true);
        return outcome with { LastError = lastError };
    }

    private VendorOutcome FallbackToCacheSilent()
    {
        var bytes = _cache.MaybePayload()
            ?? throw new TransportException("gemini: no cache and network unreachable");
        return ReuseCache(bytes, true);
    }

    private VendorOutcome HandleAuthFailure(bool transient)
    {
        var bytes = _cache.MaybePayload();
        if (bytes is null)
        {
            throw transient
                ? new TransportException("gemini: no cache and refresh failed transiently")
                : new CredentialsException("gemini token refresh failed; run `gemini` to re-auth");
        }
        return ReuseCache(bytes, true);
    }
}

using System.Text;
using AiUsageHud.Core.Abstractions;
using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Http;
using AiUsageHud.Core.Models;

namespace AiUsageHud.Core.Vendors.Antigravity;

/// <summary>
/// Fetches Antigravity usage snapshots from a local language server. Probes running
/// Antigravity 2.0 / agy / IDE instances on dynamic loopback ports, resolves the active
/// session via <c>GetUserStatus</c>, and reads quota from <c>RetrieveUserQuotaSummary</c>.
/// </summary>
public sealed class AntigravityFetcher : IVendorFetcher
{
    private const string QuotaRpc = "exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";
    private const string StatusRpc = "exa.language_server_pb.LanguageServerService/GetUserStatus";

    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _client;
    private readonly Cache _cache;
    private readonly TimeSpan _ttl;
    private readonly string? _lsAddressOverride;

    public AntigravityFetcher(HttpClient client, Cache cache, TimeSpan ttl, string? lsAddressOverride = null)
    {
        _client = client;
        _cache = cache;
        _ttl = ttl;
        _lsAddressOverride = string.IsNullOrWhiteSpace(lsAddressOverride) ? null : lsAddressOverride;
    }

    public VendorId Id => VendorId.Antigravity;

    public async Task<VendorOutcome> FetchAsync(CancellationToken ct = default)
    {
        _cache.EnsureDir();
        using var _lock = _cache.AcquireLock(LockTimeout);

        // Resolve active local session first
        Session? session = null;
        AppException? sessionError = null;
        try
        {
            session = await OpenSessionAsync(ct);
        }
        catch (AppException ex)
        {
            sessionError = ex;
        }

        // Fast path: fresh cache
        var fresh = _cache.FreshPayload(_ttl);
        if (fresh is not null)
        {
            try
            {
                var cached = AntigravityCacheDto.Deserialize(fresh);
                // If we resolved a session and the account matches (or cached has account), reuse
                if (session is null || string.IsNullOrEmpty(session.Account) ||
                    string.IsNullOrEmpty(cached.Account) || session.Account == cached.Account)
                {
                    return ReuseCache(fresh, stale: false);
                }
            }
            catch
            {
                // corrupted cache, proceed to live fetch
            }
        }

        if (session is null)
        {
            if (sessionError is HttpStatusException httpEx)
            {
                _cache.MarkStale();
                _cache.WriteLastError(httpEx.Status, httpEx.Body);
                return FallbackToCache((httpEx.Status, httpEx.Body));
            }
            if (sessionError is AppException appEx && appEx.IsTransient)
            {
                return FallbackToCacheSilent();
            }
            if (sessionError is not null)
            {
                _cache.MarkStale();
                _cache.WriteLastError(0, sessionError.Message);
                return FallbackToCache((0, sessionError.Message));
            }

            throw new CredentialsException(
                "Antigravity: no local server found. Quota is only served while Antigravity is running — open the Antigravity app, or an interactive `agy` session, or point ANTIGRAVITY_LS_ADDRESS at a host:port.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HttpTimeout);

            var quotaBytes = await PostRpcAsync(session.Base, session.Csrf, QuotaRpc, cts.Token);
            var snap = AntigravityQuotaResponse.Parse(quotaBytes).ToSnapshot(session.Plan, session.Account);

            _cache.WritePayload(AntigravityCacheDto.Serialize(snap));
            return new VendorOutcome(snap, false, null, TimeSpan.Zero);
        }
        catch (HttpStatusException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(e.Status, e.Body);
            return FallbackToCache((e.Status, e.Body));
        }
        catch (AppException e) when (e.IsTransient)
        {
            return FallbackToCacheSilent();
        }
        catch (OperationCanceledException)
        {
            return FallbackToCacheSilent();
        }
        catch (AppException e)
        {
            _cache.MarkStale();
            _cache.WriteLastError(0, e.Message);
            return FallbackToCache((0, e.Message));
        }
    }

    private sealed record Session(string Base, string? Csrf, string Plan, string Account);

    private async Task<Session> OpenSessionAsync(CancellationToken ct)
    {
        var bases = AntigravityDiscovery.CandidateBases(_lsAddressOverride);
        if (bases.Count == 0)
        {
            throw new CredentialsException(
                "Antigravity: no local server found. Quota is only served while Antigravity is running — open the Antigravity app, or an interactive `agy` session, or point ANTIGRAVITY_LS_ADDRESS at a host:port.");
        }

        var errors = new List<AppException>();
        foreach (var baseUrl in bases)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(HttpTimeout);

                var csrf = await FetchCsrfAsync(baseUrl, cts.Token);
                var statusBytes = await PostRpcAsync(baseUrl, csrf, StatusRpc, cts.Token);
                var status = AntigravityUserStatusResponse.Parse(statusBytes);

                return new Session(baseUrl, csrf, status.PlanLabel(), status.AccountKey());
            }
            catch (AppException e)
            {
                errors.Add(e);
            }
            catch (OperationCanceledException)
            {
                errors.Add(new TransportException($"antigravity: timeout probing {baseUrl}"));
            }
        }

        throw SelectProbeError(errors);
    }

    private static AppException SelectProbeError(List<AppException> errors)
    {
        AppException? actionable = null;
        AppException? last = null;
        AppException? echo = null;

        foreach (var e in errors)
        {
            if (actionable is null && isActionable(e))
                actionable = e;
            else if (isTlsEcho(e))
                echo = e;
            else
                last = e;
        }

        return actionable ?? last ?? echo ?? new OtherException("antigravity: no local server answered GetUserStatus");

        static bool isActionable(AppException e) =>
            e is HttpStatusException { Status: 401 or 403 };

        static bool isTlsEcho(AppException e) =>
            e is HttpStatusException { Status: 400, Body: var b } && b.Contains("HTTP request to an HTTPS server", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> FetchCsrfAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            using var resp = await _client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var bytes = await CappedBody.ReadAsync(resp.Content, ct);
            var html = Encoding.UTF8.GetString(bytes);

            const string tokenPrefix = "csrfToken\":\"";
            var idx = html.IndexOf(tokenPrefix, StringComparison.Ordinal);
            if (idx < 0) return null;

            var start = idx + tokenPrefix.Length;
            var end = html.IndexOf('"', start);
            if (end > start)
            {
                var token = html[start..end];
                return token.Length > 0 ? token : null;
            }
        }
        catch
        {
            // best effort
        }

        return null;
    }

    private async Task<byte[]> PostRpcAsync(string baseUrl, string? csrf, string rpc, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{rpc}")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(csrf))
        {
            req.Headers.TryAddWithoutValidation("x-codeium-csrf-token", csrf);
        }

        HttpResponseMessage resp;
        try
        {
            resp = await _client.SendAsync(req, ct);
        }
        catch (HttpRequestException e)
        {
            throw new TransportException(e.Message);
        }

        var bytes = await CappedBody.ReadAsync(resp.Content, ct);
        if (resp.IsSuccessStatusCode) return bytes;

        var body = Encoding.UTF8.GetString(bytes);
        if (body.Length > 200) body = body[..200];
        throw new HttpStatusException((int)resp.StatusCode, body);
    }

    private VendorOutcome ReuseCache(byte[] bytes, bool stale)
    {
        var snap = AntigravityCacheDto.Deserialize(bytes);
        return new VendorOutcome(snap, stale, _cache.ReadLastError(), _cache.PayloadAge());
    }

    private VendorOutcome FallbackToCache((int, string) lastError)
    {
        var bytes = _cache.FallbackPayload(Cache.MaxStale) ?? throw new OtherException("antigravity: no usable cache");
        var outcome = ReuseCache(bytes, true);
        return outcome with { LastError = lastError };
    }

    private VendorOutcome FallbackToCacheSilent()
    {
        var bytes = _cache.FallbackPayload(Cache.MaxStale)
            ?? throw new TransportException("antigravity: no cache and local server unreachable");
        return ReuseCache(bytes, true);
    }
}

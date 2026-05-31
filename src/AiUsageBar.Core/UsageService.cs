using AiUsageBar.Core.Abstractions;
using AiUsageBar.Core.Caching;
using AiUsageBar.Core.Config;
using AiUsageBar.Core.Models;
using AiUsageBar.Core.Vendors.Anthropic;
using AiUsageBar.Core.Vendors.Copilot;
using AiUsageBar.Core.Vendors.OpenAi;
using AiUsageBar.Core.Vendors.OpenRouter;
using AiUsageBar.Core.Vendors.Zai;

namespace AiUsageBar.Core;

/// <summary>The result of refreshing one vendor — either a ready outcome or an error.</summary>
public abstract record TabResult
{
    public sealed record Ready(VendorOutcome Outcome, DateTimeOffset? FetchedAt) : TabResult;
    public sealed record Error(string Message) : TabResult;
}

/// <summary>
/// Orchestrates fetching one vendor: resolves creds/keys, builds the right fetcher,
/// and returns a <see cref="TabResult"/>. Port of the Rust <c>tui::app::build_outcome</c>
/// / <c>refresh_one</c>.
/// </summary>
public sealed class UsageService
{
    private readonly HttpClient _client;
    private readonly AppConfig _config;

    public UsageService(HttpClient client, AppConfig config)
    {
        _client = client;
        _config = config;
    }

    public async Task<TabResult> RefreshAsync(VendorId vendor, CancellationToken ct = default)
    {
        try
        {
            var fetcher = BuildFetcher(vendor);
            var outcome = await fetcher.FetchAsync(ct);
            var fetchedAt = outcome.CacheAge is { } age ? DateTimeOffset.UtcNow - age : (DateTimeOffset?)null;
            return new TabResult.Ready(outcome, fetchedAt);
        }
        catch (Exception e)
        {
            return new TabResult.Error(e.Message);
        }
    }

    private IVendorFetcher BuildFetcher(VendorId vendor) => vendor switch
    {
        VendorId.Anthropic => new AnthropicFetcher(
            _client,
            _config.Anthropic.CredentialsPath ?? AppPaths.AnthropicCredentials,
            Cache.ForVendor("anthropic"),
            Cache.DefaultTtl),

        VendorId.Openai => new OpenAiFetcher(
            _client,
            _config.Openai.CodexAuthPath ?? AppPaths.OpenAiCredentials,
            Cache.ForVendor("openai"),
            Cache.DefaultTtl),

        VendorId.Copilot => new CopilotFetcher(
            _client,
            () => CopilotCreds.Resolve(_config.Copilot),
            Cache.ForVendor("copilot"),
            Cache.DefaultTtl),

        VendorId.Zai => new ZaiFetcher(
            _client,
            ApiKeyResolver.Resolve("Zai", _config.Zai.ApiKeyEnv, _config.Zai.ApiKey),
            Cache.ForVendor("zai"),
            Cache.DefaultTtl,
            _config.Zai.PlanTier),

        VendorId.Openrouter => new OpenRouterFetcher(
            _client,
            ApiKeyResolver.Resolve("OpenRouter", _config.Openrouter.ApiKeyEnv, _config.Openrouter.ApiKey),
            Cache.ForVendor("openrouter"),
            Cache.DefaultTtl),

        _ => throw new ArgumentOutOfRangeException(nameof(vendor)),
    };
}

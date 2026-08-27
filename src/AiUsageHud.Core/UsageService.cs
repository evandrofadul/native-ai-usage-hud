using AiUsageHud.Core.Abstractions;
using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Config;
using AiUsageHud.Core.Models;
using AiUsageHud.Core.Vendors.Anthropic;
using AiUsageHud.Core.Vendors.Antigravity;
using AiUsageHud.Core.Vendors.Copilot;
using AiUsageHud.Core.Vendors.Gemini;
using AiUsageHud.Core.Vendors.OpenAi;

namespace AiUsageHud.Core;

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

        VendorId.Gemini => new GeminiFetcher(
            _client,
            _config.Gemini.CredentialsPath ?? AppPaths.GeminiCredentials,
            Cache.ForVendor("gemini"),
            Cache.DefaultTtl,
            _config.Gemini.ProjectId),

        VendorId.Antigravity => new AntigravityFetcher(
            _client,
            Cache.ForVendor("antigravity"),
            Cache.DefaultTtl,
            _config.Antigravity.LanguageServerAddress),

        _ => throw new ArgumentOutOfRangeException(nameof(vendor)),
    };
}

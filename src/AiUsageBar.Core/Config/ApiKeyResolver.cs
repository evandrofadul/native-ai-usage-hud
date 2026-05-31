using AiUsageBar.Core.Errors;

namespace AiUsageBar.Core.Config;

/// <summary>
/// Resolve an API key for a vendor: env var wins, then inline config, then a
/// clear error naming both. Port of the Rust <c>resolve_api_key</c>.
/// </summary>
public static class ApiKeyResolver
{
    public static string Resolve(string vendorLabel, string envVarName, string? inline)
    {
        if (!string.IsNullOrEmpty(envVarName))
        {
            var v = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        if (!string.IsNullOrEmpty(inline)) return inline;

        throw new CredentialsException(
            $"{vendorLabel}: no API key. Either set the {envVarName} environment " +
            $"variable or set `api_key` under [{vendorLabel.ToLowerInvariant()}] in " +
            "%APPDATA%\\ai-usagebar\\config.toml.");
    }
}

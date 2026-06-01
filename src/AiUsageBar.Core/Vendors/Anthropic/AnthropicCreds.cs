using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Caching;
using AiUsageBar.Core.Errors;
using AiUsageBar.Core.Json;

namespace AiUsageBar.Core.Vendors.Anthropic;

/// <summary>
/// Read/write <c>%USERPROFILE%\.claude\.credentials.json</c> — the OAuth state the
/// Claude CLI maintains. Port of the Rust <c>anthropic/creds.rs</c>.
/// </summary>
public sealed class OauthCreds
{
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = "";

    /// <summary>Unix epoch in milliseconds (may arrive as float in the wild).</summary>
    [JsonPropertyName("expiresAt")]
    [JsonConverter(typeof(LenientLongConverter))]
    public long ExpiresAtMs { get; set; }

    [JsonPropertyName("subscriptionType")] public string SubscriptionType { get; set; } = "";
    [JsonPropertyName("rateLimitTier")] public string RateLimitTier { get; set; } = "";

    public long ExpiresAtSecs => ExpiresAtMs / 1000;

    /// <summary>Plan label "${Sub^} [5x|20x]" — capitalized subscription + optional tier.</summary>
    public string PlanLabel()
    {
        var name = Capitalize(SubscriptionType);
        if (string.IsNullOrEmpty(name)) name = "Unknown";
        if (RateLimitTier.Contains("5x")) name += " 5x";
        else if (RateLimitTier.Contains("20x")) name += " 20x";
        return name;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

public static class AnthropicCreds
{
    public static OauthCreds ReadFrom(string path)
    {
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (Exception e) { throw new CredentialsException($"could not read {path}: {e.Message}. Run `claude` to re-authenticate."); }

        try
        {
            var doc = JsonSerializer.Deserialize(raw, AppJsonContext.Default.CredentialsFile);
            if (doc?.ClaudeAiOauth is null)
                throw new CredentialsException($"{path} missing claudeAiOauth. Run `claude` to re-authenticate.");
            return doc.ClaudeAiOauth;
        }
        catch (JsonException e)
        {
            throw new CredentialsException($"could not parse {path}: {e.Message}. Run `claude` to re-authenticate.");
        }
    }

    /// <summary>
    /// Persist updated credentials, preserving unknown top-level fields the Claude
    /// CLI may have added. Atomic write.
    /// </summary>
    public static void WriteBack(string path, OauthCreds creds)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        catch { root = new JsonObject(); }

        root["claudeAiOauth"] = JsonSerializer.SerializeToNode(creds, AppJsonContext.Default.OauthCreds);
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Cache.AtomicWrite(path, bytes);
    }

    internal sealed class CredentialsFile
    {
        [JsonPropertyName("claudeAiOauth")] public OauthCreds? ClaudeAiOauth { get; set; }
    }
}

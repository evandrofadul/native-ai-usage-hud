using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Json;

namespace AiUsageHud.Core.Vendors.Gemini;

/// <summary>
/// Read/write <c>%USERPROFILE%\.gemini\oauth_creds.json</c> — the OAuth state the
/// Gemini CLI maintains (Google "authorized_user" credentials). Sibling of
/// <see cref="Anthropic.AnthropicCreds"/>; the field names are flat snake_case here.
/// </summary>
public sealed class GeminiOauthCreds
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";

    /// <summary>Unix epoch in milliseconds (Google stores the absolute expiry).</summary>
    [JsonPropertyName("expiry_date")]
    [JsonConverter(typeof(LenientLongConverter))]
    public long ExpiryDateMs { get; set; }

    public long ExpiresAtSecs => ExpiryDateMs / 1000;
}

public static class GeminiCreds
{
    public static GeminiOauthCreds ReadFrom(string path)
    {
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (Exception e) { throw new CredentialsException($"could not read {path}: {e.Message}. Run `gemini` to re-authenticate."); }

        try
        {
            var creds = JsonSerializer.Deserialize(raw, AppJsonContext.Default.GeminiOauthCreds);
            if (creds is null || string.IsNullOrEmpty(creds.AccessToken))
                throw new CredentialsException($"{path} missing access_token. Run `gemini` to re-authenticate.");
            return creds;
        }
        catch (JsonException e)
        {
            throw new CredentialsException($"could not parse {path}: {e.Message}. Run `gemini` to re-authenticate.");
        }
    }

    /// <summary>
    /// Persist refreshed tokens, preserving the other top-level fields the Gemini CLI
    /// keeps (scope, token_type, id_token, …). Atomic write.
    /// </summary>
    public static void WriteBack(string path, GeminiOauthCreds creds)
    {
        JsonObject root;
        try { root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch { root = new JsonObject(); }

        root["access_token"] = creds.AccessToken;
        root["refresh_token"] = creds.RefreshToken;
        root["expiry_date"] = creds.ExpiryDateMs;

        var bytes = System.Text.Encoding.UTF8.GetBytes(
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Cache.AtomicWrite(path, bytes);
    }
}

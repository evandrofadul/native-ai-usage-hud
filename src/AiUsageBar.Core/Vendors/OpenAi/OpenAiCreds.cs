using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Caching;
using AiUsageBar.Core.Errors;
using AiUsageBar.Core.Json;

namespace AiUsageBar.Core.Vendors.OpenAi;

/// <summary>
/// Read/write <c>%USERPROFILE%\.codex\auth.json</c>. Expiry and plan type come from
/// the JWT <c>id_token</c> (base64url payload, no signature validation). Port of
/// the Rust <c>openai/creds.rs</c>.
/// </summary>
public sealed class Tokens
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("id_token")] public string IdToken { get; set; } = "";
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }

    /// <summary>Unix-seconds expiry from the id_token; 0 forces immediate refresh.</summary>
    public long ExpiresAtSecs => ParseJwtExp(IdToken) ?? 0;

    /// <summary>Plan tier from id_token claim <c>https://api.openai.com/auth.chatgpt_plan_type</c>.</summary>
    public string? PlanTypeFromIdToken()
    {
        if (ParseJwtClaims(IdToken) is not { } claims) return null;
        if (claims.TryGetProperty("https://api.openai.com/auth", out var auth) &&
            auth.ValueKind == JsonValueKind.Object &&
            auth.TryGetProperty("chatgpt_plan_type", out var p) &&
            p.ValueKind == JsonValueKind.String)
            return p.GetString();
        return null;
    }

    private static long? ParseJwtExp(string token)
    {
        var claims = ParseJwtClaims(token);
        if (claims is null) return null;
        if (claims.Value.TryGetProperty("exp", out var exp))
        {
            if (exp.TryGetInt64(out var i)) return i;
            if (exp.TryGetDouble(out var d)) return (long)d;
        }
        return null;
    }

    private static JsonElement? ParseJwtClaims(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var bytes = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4)
        {
            case 2: t += "=="; break;
            case 3: t += "="; break;
        }
        return Convert.FromBase64String(t);
    }
}

public sealed class AuthFile
{
    [JsonPropertyName("tokens")] public Tokens Tokens { get; set; } = new();
}

public static class OpenAiCreds
{
    public static AuthFile ReadFrom(string path)
    {
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (Exception e) { throw new CredentialsException($"could not read {path}: {e.Message}. Run `codex login` to re-authenticate."); }

        try
        {
            var auth = JsonSerializer.Deserialize<AuthFile>(raw, JsonDefaults.Options);
            if (auth?.Tokens is null || string.IsNullOrEmpty(auth.Tokens.AccessToken))
                throw new CredentialsException($"{path} missing tokens. Run `codex login` to re-authenticate.");
            return auth;
        }
        catch (JsonException e)
        {
            throw new CredentialsException($"could not parse {path}: {e.Message}. Run `codex login` to re-authenticate.");
        }
    }

    /// <summary>Persist updated tokens, preserving unknown top-level fields. Atomic.</summary>
    public static void WriteBack(string path, AuthFile auth)
    {
        JsonObject root;
        try { root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch { root = new JsonObject(); }

        root["tokens"] = JsonSerializer.SerializeToNode(auth.Tokens, JsonDefaults.Options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Cache.AtomicWrite(path, bytes);
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Errors;
using AiUsageBar.Core.Json;

namespace AiUsageBar.Core.Vendors.Anthropic;

/// <summary>
/// OAuth token refresh — <c>POST https://platform.claude.com/v1/oauth/token</c>.
/// Port of the Rust <c>anthropic/oauth.rs</c>.
/// </summary>
public static class AnthropicOAuth
{
    public const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    public const string BetaHeader = "oauth-2025-04-20";
    public const string UserAgent = "claude-cli/1.0";

    /// <summary>Refresh if the token expires within this many seconds (claudebar REFRESH_BUFFER).</summary>
    public const long RefreshBufferSecs = 300;

    public static bool NeedsRefresh(long expiresAtSecs, long nowSecs) =>
        expiresAtSecs < nowSecs + RefreshBufferSecs;

    public sealed class RefreshResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        [JsonConverter(typeof(LenientLongConverter))]
        public long ExpiresIn { get; set; }
    }

    public static async Task<RefreshResponse> RefreshAsync(
        HttpClient client, string endpoint, string refreshToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Content = JsonContent.Create(new
        {
            grant_type = "refresh_token",
            client_id = ClientId,
            refresh_token = refreshToken,
        });

        HttpResponseMessage resp;
        try { resp = await client.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new TransportException(e.Message); }
        catch (TaskCanceledException e) { throw new TransportException(e.Message); }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = ParseErrorBody(body) ?? ((int)resp.StatusCode < 500 ? "Refresh failed" : "Invalid refresh response");
            throw new HttpStatusException((int)resp.StatusCode, msg);
        }

        try { return JsonSerializer.Deserialize<RefreshResponse>(body, JsonDefaults.Options)!; }
        catch (JsonException e) { throw new SchemaException($"token refresh response: {e.Message}"); }
    }

    /// <summary>Extract a human message from a non-2xx body (three claudebar shapes).</summary>
    public static string? ParseErrorBody(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error_description", out var d) && d.ValueKind == JsonValueKind.String)
                return d.GetString();
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object &&
                    err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    return m.GetString();
                if (err.ValueKind == JsonValueKind.String) return err.GetString();
            }
        }
        catch (JsonException) { return null; }
        return null;
    }
}

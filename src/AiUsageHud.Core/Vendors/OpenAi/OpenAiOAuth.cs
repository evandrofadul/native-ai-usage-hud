using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Json;
using AiUsageHud.Core.Vendors.Anthropic;

namespace AiUsageHud.Core.Vendors.OpenAi;

/// <summary>
/// OAuth refresh — <c>POST https://auth.openai.com/oauth/token</c>. The response
/// includes a fresh id_token too (we persist all three). Port of the Rust
/// <c>openai/oauth.rs</c>.
/// </summary>
public static class OpenAiOAuth
{
    public const string TokenUrl = "https://auth.openai.com/oauth/token";
    public const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    public const string Scope = "openid profile email";
    public const long RefreshBufferSecs = 300;

    public static bool NeedsRefresh(long expiresAtSecs, long nowSecs) =>
        expiresAtSecs < nowSecs + RefreshBufferSecs;

    public sealed class RefreshResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }

        [JsonPropertyName("expires_in")]
        [JsonConverter(typeof(LenientNullableLongConverter))]
        public long? ExpiresIn { get; set; }
    }

    /// <summary>Request body for the token refresh. A concrete type (not an anonymous
    /// object) so it can be serialized through the source-gen context under Native AOT.</summary>
    public sealed class RefreshRequest
    {
        [JsonPropertyName("client_id")] public string ClientId { get; set; } = "";
        [JsonPropertyName("grant_type")] public string GrantType { get; set; } = "refresh_token";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("scope")] public string Scope { get; set; } = "";
    }

    public static async Task<RefreshResponse> RefreshAsync(
        HttpClient client, string endpoint, string refreshToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(
                new RefreshRequest { ClientId = ClientId, RefreshToken = refreshToken, Scope = Scope },
                AppJsonContext.Default.OpenAiRefreshRequest),
        };

        HttpResponseMessage resp;
        try { resp = await client.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new TransportException(e.Message); }
        catch (TaskCanceledException e) { throw new TransportException(e.Message); }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = AnthropicOAuth.ParseErrorBody(body) ?? "Refresh failed";
            throw new HttpStatusException((int)resp.StatusCode, msg);
        }

        try { return JsonSerializer.Deserialize(body, AppJsonContext.Default.OpenAiRefreshResponse)!; }
        catch (JsonException e) { throw new SchemaException($"openai token response: {e.Message}"); }
    }
}

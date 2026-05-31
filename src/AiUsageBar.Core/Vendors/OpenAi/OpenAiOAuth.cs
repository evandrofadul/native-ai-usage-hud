using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Errors;
using AiUsageBar.Core.Json;
using AiUsageBar.Core.Vendors.Anthropic;

namespace AiUsageBar.Core.Vendors.OpenAi;

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

    public static async Task<RefreshResponse> RefreshAsync(
        HttpClient client, string endpoint, string refreshToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                client_id = ClientId,
                grant_type = "refresh_token",
                refresh_token = refreshToken,
                scope = Scope,
            }),
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

        try { return JsonSerializer.Deserialize<RefreshResponse>(body, JsonDefaults.Options)!; }
        catch (JsonException e) { throw new SchemaException($"openai token response: {e.Message}"); }
    }
}

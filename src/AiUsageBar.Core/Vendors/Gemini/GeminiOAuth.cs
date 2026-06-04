using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Errors;
using AiUsageBar.Core.Json;

namespace AiUsageBar.Core.Vendors.Gemini;

/// <summary>
/// OAuth token refresh against Google's token endpoint
/// (<c>POST https://oauth2.googleapis.com/token</c>, form-encoded). The client id +
/// secret are the public Gemini CLI installed-app credentials (not secret per
/// Google's OAuth2 docs for native apps). Sibling of <see cref="Anthropic.AnthropicOAuth"/>.
/// </summary>
public static class GeminiOAuth
{
    public const string TokenUrl = "https://oauth2.googleapis.com/token";

    // Public installed-app credentials baked into the Gemini CLI (packages/core oauth2.ts).
    public const string ClientId = "681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com";
    public const string ClientSecret = "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";

    /// <summary>Refresh if the token expires within this many seconds.</summary>
    public const long RefreshBufferSecs = 300;

    public static bool NeedsRefresh(long expiresAtSecs, long nowSecs) =>
        expiresAtSecs < nowSecs + RefreshBufferSecs;

    public sealed class RefreshResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";

        [JsonPropertyName("expires_in")]
        [JsonConverter(typeof(LenientLongConverter))]
        public long ExpiresIn { get; set; }
    }

    public static async Task<RefreshResponse> RefreshAsync(
        HttpClient client, string endpoint, string refreshToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("client_secret", ClientSecret),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
            }),
        };

        HttpResponseMessage resp;
        try { resp = await client.SendAsync(req, ct); }
        catch (HttpRequestException e) { throw new TransportException(e.Message); }
        catch (TaskCanceledException e) { throw new TransportException(e.Message); }

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = Anthropic.AnthropicOAuth.ParseErrorBody(body) ?? "Refresh failed";
            throw new HttpStatusException((int)resp.StatusCode, msg);
        }

        try { return JsonSerializer.Deserialize(body, AppJsonContext.Default.GeminiRefreshResponse)!; }
        catch (JsonException e) { throw new SchemaException($"gemini token response: {e.Message}"); }
    }
}

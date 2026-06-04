using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageHud.Core.Vendors.Anthropic;
using AiUsageHud.Core.Vendors.Copilot;
using AiUsageHud.Core.Vendors.Gemini;
using AiUsageHud.Core.Vendors.OpenAi;

namespace AiUsageHud.Core.Json;

/// <summary>
/// System.Text.Json source-generation context. Every DTO the app serializes or
/// deserializes is registered here so the metadata is generated at compile time —
/// no runtime reflection or <c>Reflection.Emit</c>, which is what makes the Core
/// (and therefore the Avalonia head) safe to publish with Native AOT.
///
/// The options here mirror the previous reflection-based shared defaults:
/// case-insensitive properties, numbers accepted from strings, and comments skipped.
/// Property-level <c>[JsonConverter]</c> attributes (the lenient converters in
/// <c>LenientConverters.cs</c>) are honored by the generator.
///
/// <see cref="JsonSerializableAttribute.TypeInfoPropertyName"/> is set wherever a
/// short type name would collide (the vendors' <c>RefreshRequest</c>/
/// <c>RefreshResponse</c> types).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    ReadCommentHandling = JsonCommentHandling.Skip)]
// Anthropic
[JsonSerializable(typeof(AnthropicUsageResponse))]
[JsonSerializable(typeof(OauthCreds))]
[JsonSerializable(typeof(AnthropicCreds.CredentialsFile))]
[JsonSerializable(typeof(AnthropicOAuth.RefreshResponse), TypeInfoPropertyName = "AnthropicRefreshResponse")]
[JsonSerializable(typeof(AnthropicOAuth.RefreshRequest), TypeInfoPropertyName = "AnthropicRefreshRequest")]
// OpenAI / Codex
[JsonSerializable(typeof(OpenAiUsageResponse))]
[JsonSerializable(typeof(AuthFile))]
[JsonSerializable(typeof(Tokens))]
[JsonSerializable(typeof(OpenAiOAuth.RefreshResponse), TypeInfoPropertyName = "OpenAiRefreshResponse")]
[JsonSerializable(typeof(OpenAiOAuth.RefreshRequest), TypeInfoPropertyName = "OpenAiRefreshRequest")]
// Copilot
[JsonSerializable(typeof(CopilotUserResponse))]
[JsonSerializable(typeof(CopilotCacheDto))]
// Gemini
[JsonSerializable(typeof(GeminiOauthCreds))]
[JsonSerializable(typeof(GeminiOAuth.RefreshResponse), TypeInfoPropertyName = "GeminiRefreshResponse")]
[JsonSerializable(typeof(GeminiLoadRequest))]
[JsonSerializable(typeof(GeminiLoadResponse))]
[JsonSerializable(typeof(GeminiQuotaRequest))]
[JsonSerializable(typeof(GeminiQuotaResponse))]
[JsonSerializable(typeof(GeminiCacheDto))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

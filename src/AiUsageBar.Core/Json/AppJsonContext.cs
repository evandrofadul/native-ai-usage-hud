using System.Text.Json;
using System.Text.Json.Serialization;
using AiUsageBar.Core.Vendors.Anthropic;
using AiUsageBar.Core.Vendors.Copilot;
using AiUsageBar.Core.Vendors.OpenAi;
using AiUsageBar.Core.Vendors.OpenRouter;
using AiUsageBar.Core.Vendors.Zai;

namespace AiUsageBar.Core.Json;

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
/// short type name would collide (the two vendors' <c>RefreshRequest</c>/
/// <c>RefreshResponse</c>, and the generic <c>OrEnvelope&lt;T&gt;</c> closures).
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
// OpenRouter
[JsonSerializable(typeof(OrEnvelope<CreditsData>), TypeInfoPropertyName = "OrEnvelopeCreditsData")]
[JsonSerializable(typeof(OrEnvelope<KeyData>), TypeInfoPropertyName = "OrEnvelopeKeyData")]
[JsonSerializable(typeof(OpenRouterCacheDto))]
// Copilot
[JsonSerializable(typeof(CopilotUserResponse))]
[JsonSerializable(typeof(CopilotCacheDto))]
// Z.AI
[JsonSerializable(typeof(ZaiEnvelope))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

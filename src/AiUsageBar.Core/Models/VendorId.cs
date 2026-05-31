namespace AiUsageBar.Core.Models;

/// <summary>
/// Stable vendor identifier used by config files and the UI. Port of the Rust
/// <c>VendorId</c> enum; <see cref="Slug"/> is the lowercase serialized form.
/// </summary>
public enum VendorId
{
    Anthropic,
    Openai,
    Copilot,
    Zai,
    Openrouter,
}

public static class VendorIdExtensions
{
    /// <summary>Lowercase slug used in config (anthropic/openai/zai/openrouter).</summary>
    public static string Slug(this VendorId id) => id switch
    {
        VendorId.Anthropic => "anthropic",
        VendorId.Openai => "openai",
        VendorId.Copilot => "copilot",
        VendorId.Zai => "zai",
        VendorId.Openrouter => "openrouter",
        _ => "anthropic",
    };

    /// <summary>3-letter short id shown on the tray (cld/gpt/cop/zai/opr).</summary>
    public static string ShortId(this VendorId id) => id switch
    {
        VendorId.Anthropic => "cld",
        VendorId.Openai => "gpt",
        VendorId.Copilot => "cop",
        VendorId.Zai => "zai",
        VendorId.Openrouter => "opr",
        _ => "cld",
    };

    /// <summary>Human label for the UI.</summary>
    public static string Label(this VendorId id) => id switch
    {
        VendorId.Anthropic => "Anthropic",
        VendorId.Openai => "OpenAI",
        VendorId.Copilot => "Copilot",
        VendorId.Zai => "Z.AI",
        VendorId.Openrouter => "OpenRouter",
        _ => "Anthropic",
    };

    /// <summary>
    /// Vendors shown in the UI, in canonical order. Z.AI and OpenRouter remain in
    /// the enum (and have working fetchers) but are intentionally excluded here so
    /// they no longer appear on the tabs or in Settings.
    /// </summary>
    public static IReadOnlyList<VendorId> All { get; } = new[]
    {
        VendorId.Anthropic,
        VendorId.Openai,
        VendorId.Copilot,
    };

    /// <summary>Parse a slug back to a <see cref="VendorId"/>; null if unknown.</summary>
    public static VendorId? FromSlug(string? slug) => slug?.ToLowerInvariant() switch
    {
        "anthropic" => VendorId.Anthropic,
        "openai" => VendorId.Openai,
        "copilot" => VendorId.Copilot,
        "zai" => VendorId.Zai,
        "openrouter" => VendorId.Openrouter,
        _ => null,
    };
}

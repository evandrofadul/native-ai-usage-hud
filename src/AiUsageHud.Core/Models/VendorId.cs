namespace AiUsageHud.Core.Models;

/// <summary>
/// Stable vendor identifier used by config files and the UI. Port of the Rust
/// <c>VendorId</c> enum; <see cref="Slug"/> is the lowercase serialized form.
/// </summary>
public enum VendorId
{
    Anthropic,
    Openai,
    Copilot,
    Gemini,
    Antigravity,
}

public static class VendorIdExtensions
{
    /// <summary>Lowercase slug used in config (anthropic/openai/copilot/gemini/antigravity).</summary>
    public static string Slug(this VendorId id) => id switch
    {
        VendorId.Anthropic => "anthropic",
        VendorId.Openai => "openai",
        VendorId.Copilot => "copilot",
        VendorId.Gemini => "gemini",
        VendorId.Antigravity => "antigravity",
        _ => "anthropic",
    };

    /// <summary>Human label for the UI.</summary>
    public static string Label(this VendorId id) => id switch
    {
        VendorId.Anthropic => "Anthropic",
        VendorId.Openai => "OpenAI",
        VendorId.Copilot => "Copilot",
        VendorId.Gemini => "Gemini",
        VendorId.Antigravity => "Antigravity",
        _ => "Anthropic",
    };

    /// <summary>Vendors shown in the UI, in canonical order.</summary>
    public static IReadOnlyList<VendorId> All { get; } = new[]
    {
        VendorId.Anthropic,
        VendorId.Openai,
        VendorId.Copilot,
        VendorId.Gemini,
        VendorId.Antigravity,
    };

    /// <summary>Parse a slug back to a <see cref="VendorId"/>; null if unknown.</summary>
    public static VendorId? FromSlug(string? slug) => slug?.ToLowerInvariant() switch
    {
        "anthropic" => VendorId.Anthropic,
        "openai" => VendorId.Openai,
        "copilot" => VendorId.Copilot,
        "gemini" => VendorId.Gemini,
        "antigravity" => VendorId.Antigravity,
        _ => null,
    };
}

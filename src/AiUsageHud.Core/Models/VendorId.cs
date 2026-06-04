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
}

public static class VendorIdExtensions
{
    /// <summary>Lowercase slug used in config (anthropic/openai/copilot/gemini).</summary>
    public static string Slug(this VendorId id) => id switch
    {
        VendorId.Anthropic => "anthropic",
        VendorId.Openai => "openai",
        VendorId.Copilot => "copilot",
        VendorId.Gemini => "gemini",
        _ => "anthropic",
    };

    /// <summary>Human label for the UI.</summary>
    public static string Label(this VendorId id) => id switch
    {
        VendorId.Anthropic => "Anthropic",
        VendorId.Openai => "OpenAI",
        VendorId.Copilot => "Copilot",
        VendorId.Gemini => "Gemini",
        _ => "Anthropic",
    };

    /// <summary>Vendors shown in the UI, in canonical order.</summary>
    public static IReadOnlyList<VendorId> All { get; } = new[]
    {
        VendorId.Anthropic,
        VendorId.Openai,
        VendorId.Copilot,
        VendorId.Gemini,
    };

    /// <summary>Parse a slug back to a <see cref="VendorId"/>; null if unknown.</summary>
    public static VendorId? FromSlug(string? slug) => slug?.ToLowerInvariant() switch
    {
        "anthropic" => VendorId.Anthropic,
        "openai" => VendorId.Openai,
        "copilot" => VendorId.Copilot,
        "gemini" => VendorId.Gemini,
        _ => null,
    };
}

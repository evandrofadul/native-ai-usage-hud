using AiUsageBar.Core.Models;
using Tomlyn;
using Tomlyn.Model;

namespace AiUsageBar.Core.Config;

/// <summary>
/// Config loaded from <c>%APPDATA%\ai-usagebar\config.toml</c>. Every field is
/// optional with sensible defaults — a missing file means "use defaults".
/// Port of the Rust <c>Config</c>.
/// </summary>
public sealed class AppConfig
{
    public UiConfig Ui { get; set; } = new();
    public AnthropicConfig Anthropic { get; set; } = new();
    public OpenAiConfig Openai { get; set; } = new();
    public CopilotConfig Copilot { get; set; } = new();
    public ZaiConfig Zai { get; set; } = new();
    public OpenRouterConfig Openrouter { get; set; } = new();

    public bool IsEnabled(VendorId id) => id switch
    {
        VendorId.Anthropic => Anthropic.Enabled,
        VendorId.Openai => Openai.Enabled,
        VendorId.Copilot => Copilot.Enabled,
        VendorId.Zai => Zai.Enabled,
        VendorId.Openrouter => Openrouter.Enabled,
        _ => false,
    };

    public IReadOnlyList<VendorId> EnabledVendors() =>
        VendorIdExtensions.All.Where(IsEnabled).ToList();

    /// <summary>Load from the default path; defaults when the file is absent.</summary>
    public static AppConfig Load() => LoadFrom(AppPaths.ConfigFile);

    /// <summary>Load from an explicit path. Throws only on actual parse failures.</summary>
    public static AppConfig LoadFrom(string path)
    {
        if (!File.Exists(path)) return new AppConfig();
        var text = File.ReadAllText(path);
        return Parse(text);
    }

    /// <summary>Parse TOML text into an <see cref="AppConfig"/> (tolerant of missing sections).</summary>
    public static AppConfig Parse(string toml)
    {
        var model = Tomlyn.Toml.ToModel(toml); // throws TomlException on malformed input
        var cfg = new AppConfig();

        if (Section(model, "ui") is { } uiT)
        {
            cfg.Ui.Primary = VendorIdExtensions.FromSlug(GetString(uiT, "primary"));
            cfg.Ui.Theme = ThemeIdExtensions.FromSlug(GetString(uiT, "theme"));
            cfg.Ui.Opacity = GetInt(uiT, "opacity");
            cfg.Ui.OpacityAffectsTray = GetBoolOrNull(uiT, "opacity_affects_tray");
        }

        if (Section(model, "anthropic") is { } aT)
        {
            cfg.Anthropic.Enabled = GetBool(aT, "enabled", true);
            cfg.Anthropic.CredentialsPath = GetString(aT, "credentials_path");
        }

        if (Section(model, "openai") is { } oT)
        {
            cfg.Openai.Enabled = GetBool(oT, "enabled", true);
            cfg.Openai.CodexAuthPath = GetString(oT, "codex_auth_path");
            cfg.Openai.AdminKeyEnv = GetString(oT, "admin_key_env") ?? cfg.Openai.AdminKeyEnv;
        }

        if (Section(model, "copilot") is { } cT)
        {
            cfg.Copilot.Enabled = GetBool(cT, "enabled", true);
            cfg.Copilot.OauthToken = GetString(cT, "oauth_token");
            cfg.Copilot.CredentialTarget = GetString(cT, "credential_target") ?? cfg.Copilot.CredentialTarget;
        }

        if (Section(model, "zai") is { } zT)
        {
            cfg.Zai.Enabled = GetBool(zT, "enabled", true);
            cfg.Zai.ApiKeyEnv = GetString(zT, "api_key_env") ?? cfg.Zai.ApiKeyEnv;
            cfg.Zai.ApiKey = GetString(zT, "api_key");
            cfg.Zai.PlanTier = GetString(zT, "plan_tier");
        }

        if (Section(model, "openrouter") is { } rT)
        {
            cfg.Openrouter.Enabled = GetBool(rT, "enabled", true);
            cfg.Openrouter.ApiKeyEnv = GetString(rT, "api_key_env") ?? cfg.Openrouter.ApiKeyEnv;
            cfg.Openrouter.ApiKey = GetString(rT, "api_key");
        }

        return cfg;
    }

    private static TomlTable? Section(TomlTable model, string key) =>
        model.TryGetValue(key, out var v) ? v as TomlTable : null;

    private static string? GetString(TomlTable t, string key) =>
        t.TryGetValue(key, out var v) && v is string s ? s : null;

    private static bool GetBool(TomlTable t, string key, bool fallback) =>
        t.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    private static bool? GetBoolOrNull(TomlTable t, string key) =>
        t.TryGetValue(key, out var v) && v is bool b ? b : null;

    /// <summary>TOML integers parse as <see cref="long"/>; narrow to int, null when absent.</summary>
    private static int? GetInt(TomlTable t, string key) =>
        t.TryGetValue(key, out var v) && v is long l ? (int)l : null;
}

public sealed class UiConfig
{
    /// <summary>Null falls back to Anthropic.</summary>
    public VendorId? Primary { get; set; }

    /// <summary>Null falls back to One Dark.</summary>
    public ThemeId? Theme { get; set; }

    /// <summary>Window opacity percentage (10–100). Null falls back to 100 (opaque).</summary>
    public int? Opacity { get; set; }

    /// <summary>Whether the opacity also dims the tray's custom popup/menu. Null falls back to false.</summary>
    public bool? OpacityAffectsTray { get; set; }
}

public sealed class AnthropicConfig
{
    public bool Enabled { get; set; } = true;
    public string? CredentialsPath { get; set; }
}

public sealed class OpenAiConfig
{
    public bool Enabled { get; set; } = true;
    public string? CodexAuthPath { get; set; }
    public string AdminKeyEnv { get; set; } = "OPENAI_ADMIN_KEY";
}

public sealed class CopilotConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Inline GitHub OAuth token override (wins over the credential store).</summary>
    public string? OauthToken { get; set; }

    /// <summary>
    /// Windows Credential Manager target whose blob holds the GitHub OAuth token.
    /// Defaults to the GitHub Copilot CLI entry; <c>*</c> matches the account suffix.
    /// </summary>
    public string CredentialTarget { get; set; } = "copilot-cli/https://github.com:*";
}

public sealed class ZaiConfig
{
    public bool Enabled { get; set; } = true;
    public string ApiKeyEnv { get; set; } = "ZAI_API_KEY";
    public string? ApiKey { get; set; }
    public string? PlanTier { get; set; }
}

public sealed class OpenRouterConfig
{
    public bool Enabled { get; set; } = true;
    public string ApiKeyEnv { get; set; } = "OPENROUTER_API_KEY";
    public string? ApiKey { get; set; }
}

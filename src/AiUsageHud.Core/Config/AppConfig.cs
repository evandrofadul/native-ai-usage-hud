using AiUsageHud.Core.Models;
using Tomlyn.Model;

namespace AiUsageHud.Core.Config;

/// <summary>
/// Config loaded from <c>%APPDATA%\ai-usage-hud\config.toml</c>. Every field is
/// optional with sensible defaults — a missing file means "use defaults".
/// Port of the Rust <c>Config</c>.
/// </summary>
public sealed class AppConfig
{
    public UiConfig Ui { get; set; } = new();
    public AnthropicConfig Anthropic { get; set; } = new();
    public OpenAiConfig Openai { get; set; } = new();
    public CopilotConfig Copilot { get; set; } = new();
    public GeminiConfig Gemini { get; set; } = new();

    public bool IsEnabled(VendorId id) => id switch
    {
        VendorId.Anthropic => Anthropic.Enabled,
        VendorId.Openai => Openai.Enabled,
        VendorId.Copilot => Copilot.Enabled,
        VendorId.Gemini => Gemini.Enabled,
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
            cfg.Ui.LaunchAtStartup = GetBoolOrNull(uiT, "launch_at_startup");
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

        if (Section(model, "gemini") is { } gT)
        {
            cfg.Gemini.Enabled = GetBool(gT, "enabled", true);
            cfg.Gemini.CredentialsPath = GetString(gT, "credentials_path");
            cfg.Gemini.ProjectId = GetString(gT, "project_id");
            cfg.Gemini.GroupByVariant = GetBool(gT, "group_by_variant", true);
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

    /// <summary>Whether the app registers itself to launch on login. Null falls back to false.</summary>
    public bool? LaunchAtStartup { get; set; }
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

public sealed class GeminiConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Override for <c>~/.gemini/oauth_creds.json</c>.</summary>
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// Optional Code Assist project id sent to <c>retrieveUserQuota</c>. Left null,
    /// the fetcher resolves the managed project via <c>loadCodeAssist</c>.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// When true (default), collapse the per-model quota buckets into model variants
    /// (Flash / Flash Lite / Pro …) on the tab, each showing its highest utilization.
    /// When false, every individual model is listed.
    /// </summary>
    public bool GroupByVariant { get; set; } = true;
}

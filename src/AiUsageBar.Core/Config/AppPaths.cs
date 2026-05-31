namespace AiUsageBar.Core.Config;

/// <summary>
/// Windows path resolution. The original used XDG dirs + <c>$HOME</c>; here we map
/// to the standard Windows locations:
/// <list type="bullet">
/// <item>Config: <c>%APPDATA%\ai-usagebar\config.toml</c></item>
/// <item>Cache:  <c>%LOCALAPPDATA%\ai-usagebar\&lt;vendor&gt;\</c></item>
/// <item>Anthropic creds: <c>%USERPROFILE%\.claude\.credentials.json</c></item>
/// <item>OpenAI creds:    <c>%USERPROFILE%\.codex\auth.json</c></item>
/// </list>
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "ai-usagebar";

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string ConfigFile => Path.Combine(ConfigDir, "config.toml");

    public static string CacheRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string CacheDirFor(string vendorSlug) => Path.Combine(CacheRoot, vendorSlug);

    public static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string AnthropicCredentials =>
        Path.Combine(UserProfile, ".claude", ".credentials.json");

    public static string OpenAiCredentials =>
        Path.Combine(UserProfile, ".codex", "auth.json");
}

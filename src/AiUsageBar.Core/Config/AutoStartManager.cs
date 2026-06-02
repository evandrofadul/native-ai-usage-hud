using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AiUsageBar.Core.Config;

/// <summary>
/// Cross-platform "launch at login" toggle. The mechanism is OS-specific:
/// <list type="bullet">
/// <item>Windows: an <c>HKCU\…\CurrentVersion\Run</c> registry value pointing at the exe.</item>
/// <item>Linux: an XDG autostart entry at <c>~/.config/autostart/ai-usagebar.desktop</c>.</item>
/// </list>
/// Other platforms are a no-op. <see cref="Apply"/> is idempotent, so it doubles as a
/// "sync the entry with the current exe path" call on startup.
/// </summary>
public static class AutoStartManager
{
    /// <summary>Registry value name (Windows) / desktop-entry display name.</summary>
    private const string AppName = "AI Usage Bar";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The path of the running executable, or null when it can't be resolved.</summary>
    private static string? ExecutablePath => Environment.ProcessPath;

    /// <summary>Enable or disable launching the app when the user logs in.</summary>
    public static void Apply(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows()) ApplyWindows(enabled);
            else if (OperatingSystem.IsLinux()) ApplyLinux(enabled);
        }
        catch
        {
            // Auto-start is best-effort: a locked registry hive or read-only home
            // directory must never take the app down.
        }
    }

    /// <summary>Whether an auto-start entry currently exists for this app.</summary>
    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return IsEnabledWindows();
            if (OperatingSystem.IsLinux()) return File.Exists(LinuxDesktopFile);
        }
        catch
        {
            // ignore — treat an unreadable entry as "not enabled".
        }
        return false;
    }

    // --- Windows -----------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static void ApplyWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null) return;

        if (enabled)
        {
            if (ExecutablePath is { } exe) key.SetValue(AppName, $"\"{exe}\"");
        }
        else if (key.GetValue(AppName) is not null)
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEnabledWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppName) is not null;
    }

    // --- Linux -------------------------------------------------------------

    /// <summary><c>$XDG_CONFIG_HOME/autostart</c>, falling back to <c>~/.config/autostart</c>.</summary>
    private static string LinuxAutostartDir
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var configHome = string.IsNullOrEmpty(xdg)
                ? Path.Combine(AppPaths.UserProfile, ".config")
                : xdg;
            return Path.Combine(configHome, "autostart");
        }
    }

    private static string LinuxDesktopFile => Path.Combine(LinuxAutostartDir, "ai-usagebar.desktop");

    [SupportedOSPlatform("linux")]
    private static void ApplyLinux(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(LinuxDesktopFile)) File.Delete(LinuxDesktopFile);
            return;
        }

        if (ExecutablePath is not { } exe) return;

        Directory.CreateDirectory(LinuxAutostartDir);
        var entry =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            $"Name={AppName}\n" +
            $"Exec=\"{exe}\"\n" +
            "Terminal=false\n" +
            "X-GNOME-Autostart-enabled=true\n";
        File.WriteAllText(LinuxDesktopFile, entry);
    }
}

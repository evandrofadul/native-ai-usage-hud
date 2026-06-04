namespace AiUsageHud.App.Tray;

/// <summary>
/// A system-tray presence for the app. The Windows and Linux heads share the same
/// view model and theme service but drive the tray through platform-specific shells:
/// <list type="bullet">
/// <item><see cref="WindowsTrayController"/> — Win32 <c>Shell_NotifyIcon</c> with a fully
///   Avalonia-styled context menu and hover card.</item>
/// <item><see cref="LinuxTrayController"/> — Avalonia's cross-platform <c>TrayIcon</c> +
///   <c>NativeMenu</c> (the StatusNotifierItem model owns menu rendering and gives no
///   hover/move events, so the rich card and hand-styled menu are Windows-only).</item>
/// </list>
/// </summary>
internal interface ITrayController : IDisposable
{
    /// <summary>Create and show the tray icon. Returns false if the platform shell rejected
    /// it, so the caller can fall back to showing the window instead.</summary>
    bool Start();
}

using System.Net.Http;
using System.Threading;
using System.Windows;
using AiUsageBar.App.Tray;
using AiUsageBar.App.ViewModels;
using AiUsageBar.Core;
using AiUsageBar.Core.Config;

namespace AiUsageBar.App;

/// <summary>
/// Application entry point. Wires the HTTP client, config, view model, main
/// window, and tray icon. The window starts hidden — only the tray icon appears
/// at launch; the window is shown from the tray. Closing it hides to the tray;
/// quitting happens only from the tray menu.
/// </summary>
public partial class App : Application
{
    /// <summary>Outer HTTP timeout shared by all vendors (each applies tighter per-request limits).</summary>
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Per-user mutex name that guards against a second tray instance.</summary>
    private const string SingleInstanceMutexName = @"Local\AiUsageBar.SingleInstance";

    private Mutex? _singleInstance;
    private HttpClient? _http;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private TrayIconService? _tray;

    /// <summary>True once the user chose Quit, so the window is allowed to actually close.</summary>
    public static bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Only one tray instance per user — a second launch just exits.
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _http = new HttpClient { Timeout = HttpTimeout };
        var config = LoadConfigSafe();

        // Apply the saved color theme + window transparency before any window is shown.
        Themes.ThemeManager.Apply(config.Ui.Theme ?? Core.Models.ThemeId.OneDark);
        Themes.OpacityManager.Apply(
            config.Ui.Opacity ?? Themes.OpacityManager.DefaultPercent,
            config.Ui.OpacityAffectsTray ?? false);

        _vm = new MainViewModel(config, cfg => new UsageService(_http, cfg));
        _window = new MainWindow(_vm);
        _tray = new TrayIconService(_vm, ShowWindow, Quit);

        // Start hidden: only the tray icon is shown at launch. The window opens
        // when the user picks it from the tray.

        // Kick off the initial fetch + auto-refresh loop without blocking startup.
        _ = _vm.StartAsync();
    }

    private static AppConfig LoadConfigSafe()
    {
        try { return AppConfig.Load(); }
        catch { return new AppConfig(); }
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.ShowAnimated();
        _window.Activate();
        // Toggle Topmost to force the window to the front, then restore the
        // pinned state (a pinned window must stay topmost).
        _window.Topmost = true;
        _window.Topmost = _window.IsPinned;
    }

    private void Quit()
    {
        IsShuttingDown = true;
        _vm?.Stop();
        _tray?.Dispose();
        _http?.Dispose();
        _singleInstance?.Dispose();
        Shutdown();
    }
}

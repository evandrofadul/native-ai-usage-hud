using System.Net.Http;
using System.Threading;
using AiUsageBar.AvaloniaApp.Platform;
using AiUsageBar.AvaloniaApp.Themes;
using AiUsageBar.AvaloniaApp.Tray;
using AiUsageBar.Core;
using AiUsageBar.Core.Config;
using AiUsageBar.Core.Models;
using AiUsageBar.Presentation.Theming;
using AiUsageBar.Presentation.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AiUsageBar.AvaloniaApp;

/// <summary>
/// Application entry point. Wires the HTTP client, config, view model, main window,
/// theme service and tray icon. The window starts hidden — only the tray icon appears
/// at launch; the window is shown from the tray. Closing it hides to the tray; quitting
/// happens only from the tray menu. Mirrors the WPF App.xaml.cs.
/// </summary>
public partial class App : Application
{
    /// <summary>Outer HTTP timeout shared by all vendors (each applies tighter per-request limits).</summary>
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Per-user mutex name that guards against a second tray instance.</summary>
    private const string SingleInstanceMutexName = @"Local\AiUsageBar.SingleInstance.Avalonia";

    private readonly AvaloniaThemeService _theme = new();
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private Mutex? _singleInstance;
    private HttpClient? _http;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private ITrayController? _tray;

    /// <summary>True once the user chose Quit, so the window is allowed to actually close.</summary>
    public static bool IsShuttingDown { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;

            // The window hides to the tray on close; the app only exits on an explicit quit.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Only one tray instance per user — a second launch just exits.
            _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirst);
            if (!isFirst)
            {
                _singleInstance.Dispose();
                _singleInstance = null;
                desktop.Shutdown();
                return;
            }

            _http = new HttpClient { Timeout = HttpTimeout };
            var config = LoadConfigSafe();

            // Apply the saved color theme + window transparency before any window is shown.
            _theme.Apply(config.Ui.Theme ?? ThemeId.OneDark);
            OpacityManager.Apply(
                config.Ui.Opacity ?? OpacityManager.DefaultPercent,
                config.Ui.OpacityAffectsTray ?? false);

            // Keep the OS auto-start entry in sync with the saved preference (also refreshes
            // the recorded exe path after an update/move). No-op when disabled/absent.
            AutoStartManager.Apply(config.Ui.LaunchAtStartup ?? false);

            _vm = new MainViewModel(config, cfg => new UsageService(_http, cfg), new AvaloniaDispatcher(), _theme);
            _window = new MainWindow { DataContext = _vm };

            // Per-platform tray: the Win32 head gets the rich styled menu + hover card; every
            // other OS uses the cross-platform Avalonia TrayIcon (see ITrayController).
            _tray = OperatingSystem.IsWindows()
                ? new WindowsTrayController(this, _vm, _theme, ShowWindow, Quit)
                : new LinuxTrayController(this, _vm, _theme, ShowWindow, Quit);

            // Start hidden: only the tray icon is shown at launch. The window opens
            // when the user picks it from the tray. If the tray can't be created,
            // fall back to showing the window so the app stays reachable.
            if (!_tray.Start())
                _window.Show();

            // Kick off the initial fetch + auto-refresh loop without blocking startup.
            _ = _vm.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow() => _window?.Reveal();

    private void Quit()
    {
        IsShuttingDown = true;
        _vm?.Stop();
        _tray?.Dispose();
        _http?.Dispose();
        _singleInstance?.Dispose();
        _desktop?.Shutdown();
    }

    private static AppConfig LoadConfigSafe()
    {
        try { return AppConfig.Load(); }
        catch { return new AppConfig(); }
    }
}

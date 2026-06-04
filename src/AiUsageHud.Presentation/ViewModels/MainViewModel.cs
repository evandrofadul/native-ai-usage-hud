using System.Collections.ObjectModel;
using AiUsageHud.Core;
using AiUsageHud.Core.Config;
using AiUsageHud.Core.Models;
using AiUsageHud.Core.TokenTracking;
using AiUsageHud.Presentation.Platform;
using AiUsageHud.Presentation.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUsageHud.Presentation.ViewModels;

/// <summary>
/// Top-level view model — holds the vendor tabs, drives refresh, and exposes the
/// active vendor for the tray. Port of the Rust <c>tui::app::App</c> + the watch loop.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>Background auto-refresh cadence (the cache TTL keeps API calls bounded).</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private const int PaceTolerance = 5;

    private readonly Func<AppConfig, UsageService> _serviceFactory;
    private readonly IThemeService _themeService;
    private readonly IUiTimer _timer;

    /// <summary>Reads local Claude Code token usage for the active project (Anthropic tab).</summary>
    private readonly ClaudeTokenReader _claudeTokens = new();

    /// <summary>Reads local Codex token usage for the active workspace (OpenAI tab).</summary>
    private readonly CodexTokenReader _codexTokens = new();

    /// <summary>Reads richer Claude Code stats (sessions, streaks, heatmap) for the dashboard.</summary>
    private readonly ClaudeUsageStatsReader _claudeStats = new();

    /// <summary>Reads richer Codex stats (sessions, streaks, heatmap) for the dashboard.</summary>
    private readonly CodexUsageStatsReader _codexStats = new();

    /// <summary>Reads local Gemini CLI token usage for the active project (Gemini tab).</summary>
    private readonly GeminiTokenReader _geminiTokens = new();

    /// <summary>Reads richer Gemini CLI stats (sessions, streaks, heatmap) for the dashboard.</summary>
    private readonly GeminiUsageStatsReader _geminiStats = new();

    private AppConfig _config;
    private UsageService _service;

    /// <summary>Guards against overlapping refresh cycles when one runs past the timer interval.</summary>
    private bool _refreshing;

    [ObservableProperty] private ObservableCollection<VendorTabViewModel> _tabs = [];
    [ObservableProperty] private int _selectedIndex;
    [ObservableProperty] private string _status = "";

    /// <summary>Settings overlay state — hosted inside the main window instead of a separate dialog.</summary>
    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private SettingsViewModel? _settingsVm;

    public event EventHandler? TrayChanged;

    public MainViewModel(
        AppConfig config,
        Func<AppConfig, UsageService> serviceFactory,
        IUiDispatcher dispatcher,
        IThemeService themeService)
    {
        _config = config;
        _serviceFactory = serviceFactory;
        _themeService = themeService;
        _service = serviceFactory(config);

        BuildTabs();

        _timer = dispatcher.CreateTimer(RefreshInterval);
        _timer.Tick += async () => await RefreshAllAsync();
    }

    /// <summary>Vendor currently shown on the tray.</summary>
    public VendorId? ActiveVendor =>
        Tabs.Count > 0 && SelectedIndex >= 0 && SelectedIndex < Tabs.Count
            ? Tabs[SelectedIndex].Vendor : null;

    public string? ActiveTrayLine => ActiveVendor is null ? null
        : Tabs[SelectedIndex].TrayLine;

    private void BuildTabs()
    {
        var enabled = _config.EnabledVendors();
        Tabs = new ObservableCollection<VendorTabViewModel>(
            enabled.Select(v => new VendorTabViewModel(v, _config.Gemini.GroupByVariant)));
        var primary = _config.Ui.Primary ?? VendorId.Anthropic;
        var idx = enabled.ToList().IndexOf(primary);
        SelectedIndex = idx >= 0 ? idx : 0;
    }

    /// <summary>Start the initial fetch + the periodic timer. Call once after the window loads.</summary>
    public async Task StartAsync()
    {
        _timer.Start();
        await RefreshAllAsync();
    }

    public void Stop() => _timer.Stop();

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        // Skip if a previous cycle (manual or timer) is still in flight.
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var work = Tabs.Select(async tab =>
            {
                var result = await _service.RefreshAsync(tab.Vendor);
                tab.Apply(result, now, PaceTolerance);
                await AttachTokensAsync(tab);
            });
            await Task.WhenAll(work);
            TrayChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshing = false;
        }
    }

    [RelayCommand]
    public async Task RefreshActiveAsync()
    {
        if (ActiveVendor is null) return;
        var tab = Tabs[SelectedIndex];
        var result = await _service.RefreshAsync(tab.Vendor);
        tab.Apply(result, DateTimeOffset.UtcNow, PaceTolerance);
        await AttachTokensAsync(tab);
        TrayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Append local agent token usage as a section: Claude Code on the Anthropic tab,
    /// Codex on the OpenAI tab, Gemini CLI on the Gemini tab. Read off-thread; failures
    /// are swallowed (best-effort).
    /// </summary>
    private async Task AttachTokensAsync(VendorTabViewModel tab)
    {
        try
        {
            switch (tab.Vendor)
            {
                case VendorId.Anthropic:
                    tab.SetTokens(await Task.Run(() => _claudeTokens.Read()), "Claude Code tokens");
                    tab.SetDashboard(await Task.Run(() => _claudeStats.Read()));
                    break;
                case VendorId.Openai:
                    tab.SetTokens(await Task.Run(() => _codexTokens.Read()), "Codex tokens");
                    tab.SetDashboard(await Task.Run(() => _codexStats.Read()));
                    break;
                case VendorId.Gemini:
                    tab.SetTokens(await Task.Run(() => _geminiTokens.Read()), "Gemini CLI tokens");
                    tab.SetDashboard(await Task.Run(() => _geminiStats.Read()));
                    break;
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>Cycle the active tab forward (used by the tray menu).</summary>
    public void CycleNext()
    {
        if (Tabs.Count == 0) return;
        SelectedIndex = (SelectedIndex + 1) % Tabs.Count;
    }

    /// <summary>Cycle the active tab backward (used by the tray menu).</summary>
    public void CyclePrev()
    {
        if (Tabs.Count == 0) return;
        SelectedIndex = (SelectedIndex - 1 + Tabs.Count) % Tabs.Count;
    }

    partial void OnSelectedIndexChanged(int value) => TrayChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Open the in-window settings dialog overlay.</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_config, _themeService);
        vm.Saved += async (_, _) =>
        {
            IsSettingsOpen = false;
            var reloaded = LoadConfigSafe();
            await ReloadConfigAsync(reloaded);
        };
        vm.Cancelled += (_, _) => IsSettingsOpen = false;
        SettingsVm = vm;
        IsSettingsOpen = true;
    }

    /// <summary>Dismiss the settings overlay without saving.</summary>
    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    private static AppConfig LoadConfigSafe()
    {
        try { return AppConfig.Load(); }
        catch { return new AppConfig(); }
    }

    /// <summary>Reload config (after a Settings save) and re-fetch.</summary>
    public async Task ReloadConfigAsync(AppConfig config)
    {
        _config = config;
        _service = _serviceFactory(config);
        var keepVendor = ActiveVendor;
        BuildTabs();
        if (keepVendor is { } kv)
        {
            var idx = Tabs.ToList().FindIndex(t => t.Vendor == kv);
            if (idx >= 0) SelectedIndex = idx;
        }
        await RefreshAllAsync();
    }

    public AppConfig Config => _config;
}

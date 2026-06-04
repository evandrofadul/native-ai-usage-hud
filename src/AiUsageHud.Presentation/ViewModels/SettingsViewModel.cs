using AiUsageHud.Core.Config;
using AiUsageHud.Core.Models;
using AiUsageHud.Presentation.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUsageHud.Presentation.ViewModels;

/// <summary>
/// Settings dialog — pick the primary vendor and the color theme. Every vendor
/// (Anthropic, OpenAI, Copilot, Gemini) authenticates from its own local credential
/// store, so there are no API-key fields here.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public IReadOnlyList<VendorId> Vendors { get; } = VendorIdExtensions.All;

    /// <summary>Theme entries with color swatches for the picker (see <see cref="ThemeOption"/>).</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = ThemeOption.All;

    /// <summary>Live-applies the palette swap (framework-specific), injected by the host.</summary>
    private readonly IThemeService _themeService;

    /// <summary>The theme that was active when the dialog opened — restored on Cancel.</summary>
    private readonly ThemeId _originalTheme;

    /// <summary>Transparency state when the dialog opened — restored on Cancel.</summary>
    private readonly int _originalOpacity;
    private readonly bool _originalOpacityAffectsTray;

    [ObservableProperty] private VendorId _primary;
    [ObservableProperty] private ThemeId _theme;

    /// <summary>Window opacity percentage (10–100); the slider is bound here.</summary>
    [ObservableProperty] private int _opacity;

    /// <summary>Whether the transparency also dims the tray's custom popup/menu.</summary>
    [ObservableProperty] private bool _opacityAffectsTray;

    /// <summary>Whether the app should launch automatically when the user logs in.</summary>
    [ObservableProperty] private bool _launchAtStartup;

    [ObservableProperty] private string _status = "";

    /// <summary>Raised when a save succeeds; the host reloads config + re-fetches.</summary>
    public event EventHandler? Saved;

    /// <summary>Raised when the user dismisses the dialog without saving.</summary>
    public event EventHandler? Cancelled;

    public SettingsViewModel(AppConfig config, IThemeService themeService)
    {
        _themeService = themeService;
        _primary = config.Ui.Primary ?? VendorId.Anthropic;
        _theme = config.Ui.Theme ?? ThemeId.OneDark;
        _originalTheme = themeService.Current;

        _opacity = OpacityManager.Percent;
        _opacityAffectsTray = OpacityManager.AffectsTray;
        _originalOpacity = OpacityManager.Percent;
        _originalOpacityAffectsTray = OpacityManager.AffectsTray;

        _launchAtStartup = config.Ui.LaunchAtStartup ?? false;
    }

    /// <summary>Live-preview the theme as soon as the user picks a new one.</summary>
    partial void OnThemeChanged(ThemeId value) => _themeService.Apply(value);

    /// <summary>Live-preview the window transparency as the slider moves.</summary>
    partial void OnOpacityChanged(int value) => OpacityManager.Apply(value, OpacityAffectsTray);

    /// <summary>Live-preview whether the tray popup/menu follows the transparency.</summary>
    partial void OnOpacityAffectsTrayChanged(bool value) => OpacityManager.Apply(Opacity, value);

    [RelayCommand]
    private void Save()
    {
        try
        {
            ConfigWriter.Save(AppPaths.ConfigFile, Primary, Theme, Opacity, OpacityAffectsTray, LaunchAtStartup);
            // Register/unregister the OS auto-start entry to match the saved preference.
            AutoStartManager.Apply(LaunchAtStartup);
            Status = $"Saved to {AppPaths.ConfigFile}";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Status = $"Save failed: {e.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        // Discard the live previews if the user backed out.
        if (_themeService.Current != _originalTheme) _themeService.Apply(_originalTheme);
        if (OpacityManager.Percent != _originalOpacity || OpacityManager.AffectsTray != _originalOpacityAffectsTray)
            OpacityManager.Apply(_originalOpacity, _originalOpacityAffectsTray);
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}

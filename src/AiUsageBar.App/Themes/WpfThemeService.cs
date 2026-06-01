using AiUsageBar.Core.Models;
using AiUsageBar.Presentation.Theming;

namespace AiUsageBar.App.Themes;

/// <summary>
/// WPF implementation of <see cref="IThemeService"/>: a thin adapter over the static
/// <see cref="ThemeManager"/> (which performs the actual palette <c>ResourceDictionary</c>
/// swap). App startup and the tray still drive <see cref="ThemeManager"/> directly; only
/// the shared <c>SettingsViewModel</c> needs the abstraction.
/// </summary>
public sealed class WpfThemeService : IThemeService
{
    public ThemeId Current => ThemeManager.Current;

    public void Apply(ThemeId theme) => ThemeManager.Apply(theme);

    public event Action? Changed
    {
        add => ThemeManager.Changed += value;
        remove => ThemeManager.Changed -= value;
    }
}

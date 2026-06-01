using AiUsageBar.Core.Models;

namespace AiUsageBar.Presentation.Theming;

/// <summary>
/// Swaps the active color palette at runtime. Implemented per UI head (WPF / Avalonia)
/// because the swap itself is framework-specific (a <c>ResourceDictionary</c> replace).
/// The shared <c>SettingsViewModel</c> uses it to live-preview a theme; other live
/// elements (e.g. the tray) can subscribe to <see cref="Changed"/>.
/// </summary>
public interface IThemeService
{
    /// <summary>The theme currently applied.</summary>
    ThemeId Current { get; }

    /// <summary>Apply <paramref name="theme"/>, replacing the palette in place.</summary>
    void Apply(ThemeId theme);

    /// <summary>Raised after the palette is swapped.</summary>
    event Action? Changed;
}

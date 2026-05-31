using System.Windows;
using AiUsageBar.Core.Models;

namespace AiUsageBar.App.Themes;

/// <summary>
/// Swaps the active color palette at runtime. The app's merged dictionaries keep
/// the palette at index 0 (see <c>App.xaml</c>); replacing it triggers every
/// <c>DynamicResource</c> reference in <c>Controls.xaml</c> and the views to
/// re-resolve, so the whole UI (window, tabs, gauges, tray menu) recolors live.
/// </summary>
public static class ThemeManager
{
    /// <summary>The theme currently applied.</summary>
    public static ThemeId Current { get; private set; } = ThemeId.OneDark;

    /// <summary>
    /// Raised after the palette is swapped. Lets elements that aren't in a window's
    /// visual tree (e.g. the tray <c>ContextMenu</c>) refresh themselves — those
    /// don't get the application-level DynamicResource invalidation.
    /// </summary>
    public static event Action? Changed;

    /// <summary>Apply <paramref name="theme"/>, replacing the palette dictionary in place.</summary>
    public static void Apply(ThemeId theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/Themes/Palettes/{theme.PaletteFile()}.xaml",
                UriKind.Absolute),
        };

        // Palette is index 0 (merged before Controls.xaml). Replace it if present,
        // otherwise insert at the front so DynamicResource lookups resolve to it.
        if (dictionaries.Count > 0)
            dictionaries[0] = palette;
        else
            dictionaries.Insert(0, palette);

        Current = theme;
        Changed?.Invoke();
    }
}

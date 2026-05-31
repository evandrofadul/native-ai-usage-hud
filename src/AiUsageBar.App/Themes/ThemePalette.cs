using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using AiUsageBar.Core.Models;

namespace AiUsageBar.App.Themes;

/// <summary>
/// Reads individual brushes out of any theme's palette dictionary — even one that
/// isn't the active theme. Used to render per-theme color swatches in the Settings
/// picker. Loaded dictionaries are cached, so the 23 palettes are each parsed at
/// most once.
/// </summary>
public static class ThemePalette
{
    private static readonly Dictionary<ThemeId, ResourceDictionary> Cache = new();

    /// <summary>Load (and cache) the palette resource dictionary for <paramref name="id"/>.</summary>
    public static ResourceDictionary For(ThemeId id)
    {
        if (Cache.TryGetValue(id, out var cached)) return cached;
        var dict = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/Themes/Palettes/{id.PaletteFile()}.xaml",
                UriKind.Absolute),
        };
        Cache[id] = dict;
        return dict;
    }

    /// <summary>A brush from a theme's palette by resource key (e.g. "AccentBrush").</summary>
    public static Brush Brush(ThemeId id, string key) => (Brush)For(id)[key];
}

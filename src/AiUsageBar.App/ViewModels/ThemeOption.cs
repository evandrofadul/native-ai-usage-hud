using System.Linq;
using System.Windows.Media;
using AiUsageBar.App.Themes;
using AiUsageBar.Core.Models;

namespace AiUsageBar.App.ViewModels;

/// <summary>
/// A theme entry for the Settings combo box: the <see cref="ThemeId"/>, its display
/// label, and the palette brushes shown as color swatches (background, accent, and
/// the four severity colors) so the dropdown previews each theme.
/// </summary>
public sealed class ThemeOption
{
    public ThemeId Id { get; }
    public string Label => Id.Label();

    public Brush Background { get; }
    public Brush Accent { get; }
    public Brush SevLow { get; }
    public Brush SevMid { get; }
    public Brush SevHigh { get; }
    public Brush SevCritical { get; }

    public ThemeOption(ThemeId id)
    {
        Id = id;
        Background = ThemePalette.Brush(id, "BgBrush");
        Accent = ThemePalette.Brush(id, "AccentBrush");
        SevLow = ThemePalette.Brush(id, "SevLowBrush");
        SevMid = ThemePalette.Brush(id, "SevMidBrush");
        SevHigh = ThemePalette.Brush(id, "SevHighBrush");
        SevCritical = ThemePalette.Brush(id, "SevCriticalBrush");
    }

    /// <summary>One option per theme, in canonical order. Built once and reused.</summary>
    public static IReadOnlyList<ThemeOption> All { get; } =
        ThemeIdExtensions.All.Select(id => new ThemeOption(id)).ToList();
}

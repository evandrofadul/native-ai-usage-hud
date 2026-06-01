using AiUsageBar.Core.Models;
using AiUsageBar.Core.Theming;

namespace AiUsageBar.Presentation.ViewModels;

/// <summary>
/// A theme entry for the Settings combo box: the <see cref="ThemeId"/>, its display
/// label, and the palette colors shown as swatches (background, accent, and the four
/// severity colors) so the dropdown previews each theme. Colors are exposed as
/// <c>#RRGGBB</c> hex strings (from the shared <see cref="PaletteColors"/> table); each
/// UI head converts them to its own brush type in XAML.
/// </summary>
public sealed class ThemeOption
{
    public ThemeId Id { get; }
    public string Label => Id.Label();

    public string Background { get; }
    public string Accent { get; }
    public string SevLow { get; }
    public string SevMid { get; }
    public string SevHigh { get; }
    public string SevCritical { get; }

    public ThemeOption(ThemeId id)
    {
        Id = id;
        var p = PaletteColors.For(id);
        Background = p.Bg;
        Accent = p.Accent;
        SevLow = p.SevLow;
        SevMid = p.SevMid;
        SevHigh = p.SevHigh;
        SevCritical = p.SevCritical;
    }

    /// <summary>One option per theme, in canonical order. Built once and reused.</summary>
    public static IReadOnlyList<ThemeOption> All { get; } =
        ThemeIdExtensions.All.Select(id => new ThemeOption(id)).ToList();
}

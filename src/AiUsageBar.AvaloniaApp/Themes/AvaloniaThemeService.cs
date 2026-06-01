using AiUsageBar.Core.Models;
using AiUsageBar.Core.Theming;
using AiUsageBar.Presentation.Theming;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AiUsageBar.AvaloniaApp.Themes;

/// <summary>
/// Avalonia implementation of <see cref="IThemeService"/>. Swaps the active palette
/// dictionary in place — kept at index 0 of the application's merged dictionaries (see
/// <c>App.axaml</c>). Every <c>DynamicResource</c> reference in <c>Controls.axaml</c> and
/// the views re-resolves when the palette is replaced, so the whole UI recolors live —
/// the same approach as the WPF <c>ThemeManager</c>.
///
/// The palette is built as a plain <see cref="ResourceDictionary"/> in code from the
/// shared <see cref="PaletteColors"/> table rather than by loading a palette
/// <c>.axaml</c> at runtime. Runtime XAML loading (<c>ResourceInclude</c> with a URI
/// built at runtime) goes through <c>AvaloniaXamlLoader</c>, which is not trim/AOT-safe;
/// constructing the brushes directly is. The generated <c>Themes/Palettes/*.axaml</c>
/// files remain the source the WPF head consumes and the compile-time default in
/// <c>App.axaml</c>.
/// </summary>
public sealed class AvaloniaThemeService : IThemeService
{
    /// <summary>The 15 palette brush keys, in <see cref="PaletteColors"/> field order.
    /// Must match the <c>x:Key</c>s in <c>Themes/Palettes/*.axaml</c>.</summary>
    private static readonly string[] BrushKeys =
    [
        "BgBrush", "Bg2Brush", "Bg3Brush", "HoverBrush", "FgBrush", "DimBrush",
        "AccentBrush", "BorderBrush", "BarEmptyBrush", "SevLowBrush", "SevMidBrush",
        "SevHighBrush", "SevCriticalBrush", "CloseHoverBrush", "ClosePressedBrush",
    ];

    public ThemeId Current { get; private set; } = ThemeId.OneDark;

    public event Action? Changed;

    public void Apply(ThemeId theme)
    {
        var dictionaries = Application.Current!.Resources.MergedDictionaries;
        var palette = BuildPalette(theme);

        // Palette is index 0 (merged before Controls.axaml). Replace it if present,
        // otherwise insert at the front so DynamicResource lookups resolve to it.
        if (dictionaries.Count > 0)
            dictionaries[0] = palette;
        else
            dictionaries.Insert(0, palette);

        Current = theme;
        Changed?.Invoke();
    }

    private static ResourceDictionary BuildPalette(ThemeId theme)
    {
        var c = PaletteColors.For(theme);
        var hex = new[]
        {
            c.Bg, c.Bg2, c.Bg3, c.Hover, c.Fg, c.Dim, c.Accent, c.Border, c.BarEmpty,
            c.SevLow, c.SevMid, c.SevHigh, c.SevCritical, c.CloseHover, c.ClosePressed,
        };

        var dict = new ResourceDictionary();
        for (var i = 0; i < BrushKeys.Length; i++)
            dict[BrushKeys[i]] = new SolidColorBrush(Color.Parse(hex[i]));
        return dict;
    }
}

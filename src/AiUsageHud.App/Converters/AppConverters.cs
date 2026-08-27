using System.Globalization;
using AiUsageHud.Core.Models;
using AiUsageHud.Core.Pacing;
using AiUsageHud.Presentation.ViewModels;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AiUsageHud.App.Converters;

/// <summary>
/// Maps a <see cref="PaceSeverity"/> to the active theme's gauge color (Low→green,
/// Mid→yellow, High→orange, Critical→red), pulled from the current palette resources
/// (<c>SevLowBrush</c>…<c>SevCriticalBrush</c>). Used by the tray; in the window the
/// gauge/severity text recolor via class-based styles so they update live on a theme
/// swap. One Dark values are the fallback if a resource is missing.
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    private static readonly IBrush Green = Freeze("#98C379");
    private static readonly IBrush Yellow = Freeze("#E5C07B");
    private static readonly IBrush Orange = Freeze("#D19A66");
    private static readonly IBrush Red = Freeze("#E06C75");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PaceSeverity s) return Resource("SevLowBrush", Green);
        return s switch
        {
            PaceSeverity.Low => Resource("SevLowBrush", Green),
            PaceSeverity.Mid => Resource("SevMidBrush", Yellow),
            PaceSeverity.High => Resource("SevHighBrush", Orange),
            PaceSeverity.Critical => Resource("SevCriticalBrush", Red),
            _ => Resource("SevLowBrush", Green),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush Resource(string key, IBrush fallback) =>
        Application.Current is { } app && app.TryGetResource(key, null, out var v) && v is IBrush b ? b : fallback;

    private static IBrush Freeze(string hex) => new ImmutableSolidColorBrush(Color.Parse(hex));
}

/// <summary>
/// Renders an enum value as its human label for the Settings combo boxes:
/// <see cref="VendorId"/> and <see cref="ThemeId"/> each have a <c>Label()</c> helper.
/// </summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            VendorId v => v.Label(),
            ThemeId t => t.Label(),
            _ => value?.ToString() ?? "",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a <c>#RRGGBB</c> hex string (a <c>ThemeOption</c> swatch color from the
/// shared <c>PaletteColors</c> table) to a <see cref="SolidColorBrush"/>.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && !string.IsNullOrWhiteSpace(hex)
            ? new ImmutableSolidColorBrush(Color.Parse(hex))
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="ThemeId"/> to its <see cref="ThemeOption"/> (and back). Avalonia's
/// ComboBox has no <c>SelectedValue</c>/<c>SelectedValuePath</c>, so the theme picker
/// binds <c>SelectedItem</c> to the bound <c>ThemeId</c> through this converter.
/// </summary>
public sealed class ThemeIdToOptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ThemeId id ? ThemeOption.All.FirstOrDefault(o => o.Id == id) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ThemeOption opt ? opt.Id : null;
}

/// <summary>
/// Maps a <see cref="VendorId"/> to its brand glyph geometry (the <c>IconVendor*</c>
/// resources in Icons.axaml), so each vendor tab pill can show its logo via a PathIcon.
/// Returns the shared <see cref="Geometry"/> resource; null if it can't be resolved.
/// </summary>
public sealed class VendorToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            VendorId.Anthropic => "IconVendorAnthropic",
            VendorId.Openai => "IconVendorOpenAI",
            VendorId.Copilot => "IconVendorCopilot",
            VendorId.Gemini => "IconVendorGemini",
            VendorId.Antigravity => "IconVendorAntigravity",
            _ => null,
        };
        return key is not null && Application.Current is { } app
            && app.TryGetResource(key, null, out var g) ? g : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// True when the bound value equals the <c>ConverterParameter</c> (compared by string).
/// Drives class-based styling that stands in for WPF's <c>DataTrigger</c>: severity
/// gauge/text colors and heatmap intensity levels (e.g.
/// <c>Classes.lvl2="{Binding Level, Converter={StaticResource Eq}, ConverterParameter=2}"</c>).
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

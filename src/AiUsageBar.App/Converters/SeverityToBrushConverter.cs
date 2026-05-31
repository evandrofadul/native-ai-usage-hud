using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AiUsageBar.Core.Pacing;

namespace AiUsageBar.App.Converters;

/// <summary>
/// Maps a <see cref="PaceSeverity"/> to the active theme's gauge color:
/// Low→green, Mid→yellow, High→orange, Critical→red. Colors are pulled from the
/// current palette resources (<c>SevLowBrush</c>…<c>SevCriticalBrush</c>) so they
/// follow the selected theme; the One Dark values act as a fallback if a resource
/// is missing (e.g. at design time).
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    private static readonly Brush Green = Freeze("#98C379");
    private static readonly Brush Yellow = Freeze("#E5C07B");
    private static readonly Brush Orange = Freeze("#D19A66");
    private static readonly Brush Red = Freeze("#E06C75");

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
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

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>Look up a theme brush by key, falling back to a frozen One Dark default.</summary>
    private static Brush Resource(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static Brush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AiUsageBar.App.Converters;

/// <summary>
/// Converts a <c>#RRGGBB</c> hex string (e.g. a <c>ThemeOption</c> swatch color from the
/// shared <c>PaletteColors</c> table) to a frozen <see cref="SolidColorBrush"/>.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex)) return null;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

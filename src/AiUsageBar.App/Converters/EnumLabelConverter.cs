using System.Globalization;
using System.Windows.Data;
using AiUsageBar.Core.Models;

namespace AiUsageBar.App.Converters;

/// <summary>
/// Renders an enum value as its human label for display in the Settings combo boxes:
/// <see cref="VendorId"/> and <see cref="ThemeId"/> each have a <c>Label()</c> helper.
/// Falls back to <see cref="object.ToString"/> for anything else.
/// </summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            VendorId v => v.Label(),
            ThemeId t => t.Label(),
            _ => value?.ToString() ?? "",
        };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

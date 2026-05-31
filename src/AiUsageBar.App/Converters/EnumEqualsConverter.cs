using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiUsageBar.App.Converters;

/// <summary>
/// Two-way bind a bound enum value to a radio-button group: each radio passes the
/// enum member it represents as <c>ConverterParameter</c>. Checked iff the bound
/// value equals the parameter; checking writes the parameter back.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.Equals(parameter) ?? false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter : Binding.DoNothing;
}

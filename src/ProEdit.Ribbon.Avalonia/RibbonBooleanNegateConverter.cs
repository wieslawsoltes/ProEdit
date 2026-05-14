using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ProEdit.Ribbon.Avalonia;

public sealed class RibbonBooleanNegateConverter : IValueConverter
{
    public static readonly RibbonBooleanNegateConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool flag ? !flag : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

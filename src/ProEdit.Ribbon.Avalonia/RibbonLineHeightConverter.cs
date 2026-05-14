using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ProEdit.Ribbon.Avalonia;

public sealed class RibbonLineHeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double height && height > 0d)
        {
            return height;
        }

        if (value is float floatHeight && floatHeight > 0f)
        {
            return (double)floatHeight;
        }

        return AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

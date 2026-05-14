using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ProEdit.Ribbon.Avalonia;

public sealed class RibbonFontSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var defaultSize = 14d;
        if (parameter is string parameterText
            && double.TryParse(parameterText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            defaultSize = parsed;
        }

        if (value is double doubleValue)
        {
            return doubleValue;
        }

        if (value is float floatValue)
        {
            return (double)floatValue;
        }

        return defaultSize;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

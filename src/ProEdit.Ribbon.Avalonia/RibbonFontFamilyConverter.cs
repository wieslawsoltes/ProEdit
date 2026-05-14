using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ProEdit.Ribbon.Avalonia;

public sealed class RibbonFontFamilyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FontFamily fontFamily)
        {
            return fontFamily;
        }

        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                return new FontFamily(text);
            }
            catch (ArgumentException)
            {
                return AvaloniaProperty.UnsetValue;
            }
        }

        return AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

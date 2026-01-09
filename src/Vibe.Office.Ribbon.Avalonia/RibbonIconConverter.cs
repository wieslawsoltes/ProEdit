using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Vibe.Office.Ribbon.Avalonia;

public sealed class RibbonIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key)
        {
            return parameter is not null ? false : string.Empty;
        }

        var iconText = RibbonIconResolver.ResolveText(key);
        if (parameter is not null)
        {
            return !string.IsNullOrWhiteSpace(iconText);
        }

        return iconText ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

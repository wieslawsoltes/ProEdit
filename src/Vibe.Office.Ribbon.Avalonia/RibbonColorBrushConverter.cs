using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Vibe.Office.Ribbon;
using Vibe.Office.Primitives;

namespace Vibe.Office.Ribbon.Avalonia;

public sealed class RibbonColorBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RibbonColorItem colorItem)
        {
            return ResolveBrush(colorItem);
        }

        if (value is DocColor color)
        {
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }

        return AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    public static IBrush ResolveBrush(RibbonColorItem? colorItem)
    {
        if (colorItem is null)
        {
            return Brushes.Transparent;
        }

        if (colorItem.Color is { } color)
        {
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }

        return Brushes.Transparent;
    }
}

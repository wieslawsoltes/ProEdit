using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using ProEdit.Ribbon;

namespace ProEdit.Ribbon.Avalonia;

public sealed class RibbonParagraphSpacingConverter : IValueConverter
{
    private const double BasePadding = 2d;
    private const double HorizontalPadding = 4d;
    private const double SpacingScale = 0.05d;
    private const double MaxExtra = 8d;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not RibbonParagraphSpacingPreview spacing)
        {
            return new Thickness(HorizontalPadding, BasePadding, HorizontalPadding, BasePadding);
        }

        var top = BasePadding + Clamp(spacing.Before);
        var bottom = BasePadding + Clamp(spacing.After);
        return new Thickness(HorizontalPadding, top, HorizontalPadding, bottom);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static double Clamp(float? value)
    {
        if (!value.HasValue)
        {
            return 0d;
        }

        var scaled = value.Value * SpacingScale;
        if (scaled < 0d)
        {
            return 0d;
        }

        if (scaled > MaxExtra)
        {
            return MaxExtra;
        }

        return scaled;
    }
}

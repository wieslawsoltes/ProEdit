using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ProEdit.Ribbon;

namespace ProEdit.Ribbon.Avalonia;

public sealed class RibbonContextualAccentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var accentKey = value switch
        {
            RibbonContextualTabSet set => set.AccentKey,
            string key => key,
            _ => null
        };

        var brush = ResolveBrush(accentKey);
        if (brush is not null)
        {
            return brush;
        }

        return ResolveBrush(null) ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static IBrush? ResolveBrush(string? accentKey)
    {
        if (!string.IsNullOrWhiteSpace(accentKey))
        {
            var contextualKey = $"RibbonContextualAccent.{accentKey}";
            if (TryResolveBrush(contextualKey, out var contextualBrush))
            {
                return contextualBrush;
            }
        }

        if (TryResolveBrush("RibbonAccentBrush", out var accentBrush))
        {
            return accentBrush;
        }

        return null;
    }

    private static bool TryResolveBrush(string key, out IBrush? brush)
    {
        brush = null;
        if (Application.Current is null)
        {
            return false;
        }

        if (!Application.Current.TryFindResource(key, out var resource))
        {
            return false;
        }

        switch (resource)
        {
            case IBrush resourceBrush:
                brush = resourceBrush;
                return true;
            case Color color:
                brush = new SolidColorBrush(color);
                return true;
            default:
                return false;
        }
    }
}

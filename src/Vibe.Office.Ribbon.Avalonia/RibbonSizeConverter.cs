using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Vibe.Office.Ribbon;

namespace Vibe.Office.Ribbon.Avalonia;

public sealed class RibbonSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not RibbonControlSize size)
        {
            size = RibbonControlSize.Medium;
        }

        var mode = parameter as string;
        return mode switch
        {
            "IconSize" => size switch
            {
                RibbonControlSize.Large => GetResource("RibbonButtonLargeIconSize", 28d),
                RibbonControlSize.Medium => GetResource("RibbonButtonMediumIconSize", 20d),
                _ => GetResource("RibbonButtonSmallIconSize", 16d)
            },
            "IconFontSize" => size switch
            {
                RibbonControlSize.Large => GetResource("RibbonButtonLargeIconFontSize", 18d),
                RibbonControlSize.Medium => GetResource("RibbonButtonMediumIconFontSize", 16d),
                _ => GetResource("RibbonButtonSmallIconFontSize", 14d)
            },
            "RowSpacing" => size == RibbonControlSize.Large
                ? GetResource("RibbonButtonLargeRowSpacing", 2d)
                : GetResource("RibbonButtonSmallRowSpacing", 0d),
            "ColumnSpacing" => size switch
            {
                RibbonControlSize.Large => GetResource("RibbonButtonLargeColumnSpacing", 4d),
                RibbonControlSize.Medium => GetResource("RibbonButtonMediumColumnSpacing", 6d),
                _ => GetResource("RibbonButtonSmallColumnSpacing", 6d)
            },
            "Padding" => size switch
            {
                RibbonControlSize.Large => GetResource("RibbonButtonLargePadding", new Thickness(8, 6)),
                RibbonControlSize.Medium => GetResource("RibbonButtonMediumPadding", new Thickness(8, 4)),
                _ => GetResource("RibbonButtonSmallPadding", new Thickness(6, 2))
            },
            "MinWidth" => size switch
            {
                RibbonControlSize.Large => GetResource("RibbonButtonLargeMinWidth", 88d),
                RibbonControlSize.Medium => GetResource("RibbonButtonMediumMinWidth", 76d),
                _ => GetResource("RibbonButtonSmallMinWidth", 64d)
            },
            "MinHeight" => size switch
            {
                RibbonControlSize.Large => GetResource("RibbonButtonLargeMinHeight", 80d),
                RibbonControlSize.Medium => GetResource("RibbonButtonMediumMinHeight", 52d),
                _ => GetResource("RibbonButtonSmallMinHeight", 24d)
            },
            "ArrowWidth" => GetResource("RibbonButtonArrowWidth", 18d),
            "FontSize" => size switch
            {
                RibbonControlSize.Large => GetResource("RibbonButtonLargeFontSize", 12d),
                RibbonControlSize.Medium => GetResource("RibbonButtonMediumFontSize", 12d),
                _ => GetResource("RibbonButtonSmallFontSize", 11d)
            },
            "ContentVerticalAlignment" => size == RibbonControlSize.Large
                ? VerticalAlignment.Top
                : VerticalAlignment.Center,
            "TextAlignment" => size == RibbonControlSize.Large
                ? TextAlignment.Center
                : TextAlignment.Left,
            "TextWrapping" => size == RibbonControlSize.Large
                ? TextWrapping.Wrap
                : TextWrapping.NoWrap,
            "TextTrimming" => size == RibbonControlSize.Large
                ? TextTrimming.None
                : TextTrimming.None,
            "HorizontalAlignment" => size == RibbonControlSize.Large
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left,
            "IconRow" => size == RibbonControlSize.Large ? 0 : 0,
            "IconColumn" => size == RibbonControlSize.Large ? 0 : 0,
            "IconColumnSpan" => size == RibbonControlSize.Large ? 2 : 1,
            "TextRow" => size == RibbonControlSize.Large ? 1 : 0,
            "TextColumn" => size == RibbonControlSize.Large ? 0 : 1,
            "TextColumnSpan" => size == RibbonControlSize.Large ? 2 : 1,
            _ => AvaloniaProperty.UnsetValue
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static T GetResource<T>(string key, T fallback)
    {
        if (Application.Current?.TryFindResource(key, out var resource) == true && resource is T typed)
        {
            return typed;
        }

        return fallback;
    }
}

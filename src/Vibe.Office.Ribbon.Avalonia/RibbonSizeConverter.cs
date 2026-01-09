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
                RibbonControlSize.Large => 28d,
                RibbonControlSize.Medium => 20d,
                _ => 16d
            },
            "IconFontSize" => size switch
            {
                RibbonControlSize.Large => 18d,
                RibbonControlSize.Medium => 16d,
                _ => 14d
            },
            "RowSpacing" => size == RibbonControlSize.Large ? 4d : 0d,
            "ColumnSpacing" => size switch
            {
                RibbonControlSize.Large => 4d,
                RibbonControlSize.Medium => 6d,
                _ => 4d
            },
            "Padding" => size switch
            {
                RibbonControlSize.Large => new Thickness(8, 6),
                RibbonControlSize.Medium => new Thickness(8, 4),
                _ => new Thickness(6, 2)
            },
            "MinWidth" => size switch
            {
                RibbonControlSize.Large => 84d,
                RibbonControlSize.Medium => 72d,
                _ => 56d
            },
            "MinHeight" => size switch
            {
                RibbonControlSize.Large => 80d,
                RibbonControlSize.Medium => 52d,
                _ => 24d
            },
            "ArrowWidth" => 22d,
            "FontSize" => size switch
            {
                RibbonControlSize.Large => 12d,
                RibbonControlSize.Medium => 12d,
                _ => 11d
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
}

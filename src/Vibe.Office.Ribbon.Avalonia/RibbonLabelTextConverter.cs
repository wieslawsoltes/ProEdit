using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Vibe.Office.Ribbon;

namespace Vibe.Office.Ribbon.Avalonia;

public sealed class RibbonLabelTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IRibbonControl control)
        {
            return value?.ToString() ?? string.Empty;
        }

        if (control.LayoutSize == RibbonControlSize.Small && !string.IsNullOrWhiteSpace(control.CompactLabel))
        {
            return control.CompactLabel;
        }

        return control.Label;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

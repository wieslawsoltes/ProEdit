using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Vibe.Office.Ribbon;

namespace Vibe.Office.Ribbon.Avalonia;

public sealed class RibbonLabelVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IRibbonControl control)
        {
            return true;
        }

        switch (control.LabelMode)
        {
            case RibbonLabelMode.ForceVisible:
                return true;
            case RibbonLabelMode.ForceHidden:
                return false;
        }

        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

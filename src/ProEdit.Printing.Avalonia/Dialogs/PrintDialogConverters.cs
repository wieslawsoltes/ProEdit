using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ProEdit.Printing;
using ProEdit.Printing.Avalonia;

namespace ProEdit.Printing.Avalonia;

public sealed class PrintEnumDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            PrintRangeKind.All => "Print All Pages",
            PrintRangeKind.CurrentPage => "Print Current Page",
            PrintRangeKind.Selection => "Print Selection",
            PrintRangeKind.CustomPages => "Custom Range",
            PrintDuplexMode.Default => "Printer Default",
            PrintDuplexMode.OneSided => "Print One Sided",
            PrintDuplexMode.TwoSidedLongEdge => "Print on Both Sides (Long Edge)",
            PrintDuplexMode.TwoSidedShortEdge => "Print on Both Sides (Short Edge)",
            PrintColorMode.Color => "Color",
            PrintColorMode.Grayscale => "Grayscale",
            PrintOrientationMode.Auto => "Automatic",
            PrintOrientationMode.Portrait => "Portrait",
            PrintOrientationMode.Landscape => "Landscape",
            PrintScalingMode.FitToPage => "Fit to Page",
            PrintScalingMode.ActualSize => "Actual Size",
            PrintScalingMode.Custom => "Custom Scale",
            PrintOutputKind.Printer => "Printer",
            PrintOutputKind.Pdf => "Save as PDF",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}

public sealed class ByteArrayToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}

public sealed class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return false;
        }

        if (parameter is string text && value is Enum)
        {
            var parsed = Enum.Parse(value.GetType(), text);
            return Equals(value, parsed);
        }

        return Equals(value, parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is not null)
        {
            if (parameter is string text && targetType.IsEnum)
            {
                return Enum.Parse(targetType, text);
            }

            return parameter;
        }

        return AvaloniaProperty.UnsetValue;
    }
}

public sealed class StringHasValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string text && !string.IsNullOrWhiteSpace(text);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

public sealed class PreviewSelectionModeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is PreviewSelectionMode mode && mode == PreviewSelectionMode.Multiple
            ? SelectionMode.Multiple
            : SelectionMode.Single;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

public sealed class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool flag && !flag)
        {
            return 0.35;
        }

        return 1.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

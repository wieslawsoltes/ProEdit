using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using ProEdit.Pdf;

namespace ProEdit.Word.Avalonia;

public sealed class PdfEnumDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            PdfImportMode.Reflow => "Reflow (editable)",
            PdfImportMode.FixedLayout => "Fixed layout (preserve geometry)",
            PdfPreservationMode.None => "Do not preserve",
            PdfPreservationMode.StoreOriginal => "Store original PDF (metadata only)",
            PdfPreservationMode.Incremental => "Incremental update (best effort)",
            _ => value?.ToString()
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value ?? string.Empty;
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
            return Enum.TryParse(value.GetType(), text, ignoreCase: false, out var parsed) && Equals(value, parsed);
        }

        return Equals(value, parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is not null)
        {
            if (parameter is string text && targetType.IsEnum)
            {
                return Enum.TryParse(targetType, text, ignoreCase: false, out var parsed)
                    ? parsed
                    : AvaloniaProperty.UnsetValue;
            }

            return parameter;
        }

        return AvaloniaProperty.UnsetValue;
    }
}

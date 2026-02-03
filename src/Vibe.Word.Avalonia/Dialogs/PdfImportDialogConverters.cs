using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Vibe.Office.Pdf;

namespace Vibe.Word.Avalonia;

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
        if (parameter is string text && value is Enum)
        {
            var parsed = Enum.Parse(value.GetType(), text);
            return Equals(value, parsed);
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string text && targetType.IsEnum)
        {
            return Enum.Parse(targetType, text);
        }

        return value;
    }
}

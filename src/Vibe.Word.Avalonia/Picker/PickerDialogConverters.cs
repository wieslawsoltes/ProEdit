using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Vibe.Word.Avalonia;

public sealed class PickerGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not null)
        {
            return value is string text && !string.IsNullOrWhiteSpace(text);
        }

        if (value is not string data || string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            return Geometry.Parse(data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class PickerStringHasValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string text && !string.IsNullOrWhiteSpace(text);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ProEdit.Word.Avalonia;

internal sealed class CollabColorBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string colorText || string.IsNullOrWhiteSpace(colorText))
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        try
        {
            return new SolidColorBrush(Color.Parse(colorText));
        }
        catch (Exception)
        {
            return new SolidColorBrush(Colors.Transparent);
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is SolidColorBrush brush ? brush.Color.ToString() : null;
    }
}

internal sealed class CollabNullableIntConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is int number)
        {
            return number.ToString(culture);
        }

        if (value is IConvertible convertible)
        {
            try
            {
                return convertible.ToInt32(culture).ToString(culture);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.Integer, culture, out var number) ? number : null;
    }
}

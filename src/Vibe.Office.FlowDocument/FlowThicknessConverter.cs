using System.ComponentModel;
using System.Globalization;

namespace Vibe.Office.FlowDocument;

/// <summary>
/// Converts string values to <see cref="FlowThickness"/> instances.
/// </summary>
public sealed class FlowThicknessConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        if (sourceType == typeof(string))
        {
            return true;
        }

        return base.CanConvertFrom(context, sourceType);
    }

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text && FlowThickness.TryParse(text, out var thickness))
        {
            return thickness;
        }

        return base.ConvertFrom(context, culture, value);
    }
}

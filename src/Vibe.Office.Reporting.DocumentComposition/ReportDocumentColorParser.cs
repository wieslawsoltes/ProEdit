using System.Globalization;
using Vibe.Office.Primitives;

namespace Vibe.Office.Reporting.DocumentComposition;

internal static class ReportDocumentColorParser
{
    public static bool TryParse(string? value, out DocColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.Trim().AsSpan();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        if (span.Length == 6
            && byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            color = new DocColor(r, g, b);
            return true;
        }

        if (span.Length == 8
            && byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
            && byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(span.Slice(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
        {
            color = DocColor.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }
}

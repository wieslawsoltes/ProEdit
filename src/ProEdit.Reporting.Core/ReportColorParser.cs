using System.Drawing;
using System.Globalization;
using ProEdit.Primitives;

namespace ProEdit.Reporting;

internal static class ReportColorParser
{
    public static bool TryParse(string? value, out DocColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgb(normalized, out color);
        }

        var span = normalized.AsSpan();
        if (normalized.StartsWith('#'))
        {
            span = span[1..];
        }

        if (TryParseHex(span, out color))
        {
            return true;
        }

        var named = Color.FromName(normalized);
        if (named.IsKnownColor || named.IsNamedColor)
        {
            color = new DocColor(named.R, named.G, named.B, named.A);
            return true;
        }

        return false;
    }

    private static bool TryParseHex(ReadOnlySpan<char> span, out DocColor color)
    {
        color = default;

        if (span.Length == 3)
        {
            if (TryParseNibble(span[0], out var r3)
                && TryParseNibble(span[1], out var g3)
                && TryParseNibble(span[2], out var b3))
            {
                color = new DocColor((byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17));
                return true;
            }

            return false;
        }

        if (span.Length == 4)
        {
            if (TryParseNibble(span[0], out var a4)
                && TryParseNibble(span[1], out var r4)
                && TryParseNibble(span[2], out var g4)
                && TryParseNibble(span[3], out var b4))
            {
                color = DocColor.FromArgb((byte)(a4 * 17), (byte)(r4 * 17), (byte)(g4 * 17), (byte)(b4 * 17));
                return true;
            }

            return false;
        }

        if (span.Length == 6
            && byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r6)
            && byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g6)
            && byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b6))
        {
            color = new DocColor(r6, g6, b6);
            return true;
        }

        if (span.Length == 8
            && byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a8)
            && byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r8)
            && byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g8)
            && byte.TryParse(span.Slice(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b8))
        {
            color = DocColor.FromArgb(a8, r8, g8, b8);
            return true;
        }

        return false;
    }

    private static bool TryParseRgb(string value, out DocColor color)
    {
        color = default;
        var start = value.IndexOf('(');
        var end = value.LastIndexOf(')');
        if (start < 0 || end <= start)
        {
            return false;
        }

        var content = value.Substring(start + 1, end - start - 1);
        var parts = content.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return false;
        }

        if (!TryParseComponent(parts[0], out var r)
            || !TryParseComponent(parts[1], out var g)
            || !TryParseComponent(parts[2], out var b))
        {
            return false;
        }

        byte a = 255;
        if (parts.Length >= 4 && TryParseAlpha(parts[3], out var alpha))
        {
            a = alpha;
        }

        color = new DocColor(r, g, b, a);
        return true;
    }

    private static bool TryParseComponent(string text, out byte value)
    {
        value = 0;
        var trimmed = text.Trim();
        if (trimmed.EndsWith('%')
            && double.TryParse(trimmed.AsSpan(0, trimmed.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            percent = Math.Clamp(percent, 0d, 100d);
            value = (byte)Math.Round(percent * 2.55d, MidpointRounding.AwayFromZero);
            return true;
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        number = Math.Clamp(number, 0d, 255d);
        value = (byte)Math.Round(number, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryParseAlpha(string text, out byte value)
    {
        value = 255;
        var trimmed = text.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        if (number <= 1d)
        {
            number = Math.Clamp(number, 0d, 1d);
            value = (byte)Math.Round(number * 255d, MidpointRounding.AwayFromZero);
            return true;
        }

        number = Math.Clamp(number, 0d, 255d);
        value = (byte)Math.Round(number, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryParseNibble(char value, out int nibble)
    {
        nibble = 0;
        if (value >= '0' && value <= '9')
        {
            nibble = value - '0';
            return true;
        }

        if (value >= 'a' && value <= 'f')
        {
            nibble = value - 'a' + 10;
            return true;
        }

        if (value >= 'A' && value <= 'F')
        {
            nibble = value - 'A' + 10;
            return true;
        }

        return false;
    }
}

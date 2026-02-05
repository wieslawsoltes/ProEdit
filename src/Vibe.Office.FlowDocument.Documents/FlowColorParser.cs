using System.Globalization;
using Vibe.Office.Primitives;

namespace Vibe.Office.FlowDocument.Documents;

internal static class FlowColorParser
{
    private static readonly Dictionary<string, DocColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = DocColor.Black,
        ["white"] = DocColor.White,
        ["red"] = new DocColor(255, 0, 0),
        ["green"] = new DocColor(0, 128, 0),
        ["blue"] = new DocColor(0, 0, 255),
        ["gray"] = new DocColor(128, 128, 128),
        ["grey"] = new DocColor(128, 128, 128),
        ["yellow"] = new DocColor(255, 255, 0),
        ["magenta"] = new DocColor(255, 0, 255),
        ["cyan"] = new DocColor(0, 255, 255),
        ["transparent"] = DocColor.Transparent
    };

    public static bool TryParse(string? text, out DocColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('#'))
        {
            return TryParseHex(value.AsSpan(1), out color);
        }

        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgb(value, out color);
        }

        if (NamedColors.TryGetValue(value, out color))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, out DocColor color)
    {
        color = default;
        if (hex.Length == 3)
        {
            if (TryParseNibble(hex[0], out var r)
                && TryParseNibble(hex[1], out var g)
                && TryParseNibble(hex[2], out var b))
            {
                color = new DocColor((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
                return true;
            }

            return false;
        }

        if (hex.Length == 4)
        {
            if (TryParseNibble(hex[0], out var a)
                && TryParseNibble(hex[1], out var r)
                && TryParseNibble(hex[2], out var g)
                && TryParseNibble(hex[3], out var b))
            {
                color = new DocColor((byte)(r * 17), (byte)(g * 17), (byte)(b * 17), (byte)(a * 17));
                return true;
            }

            return false;
        }

        if (hex.Length == 6)
        {
            if (TryParseByte(hex.Slice(0, 2), out var r)
                && TryParseByte(hex.Slice(2, 2), out var g)
                && TryParseByte(hex.Slice(4, 2), out var b))
            {
                color = new DocColor(r, g, b);
                return true;
            }

            return false;
        }

        if (hex.Length == 8)
        {
            if (TryParseByte(hex.Slice(0, 2), out var a)
                && TryParseByte(hex.Slice(2, 2), out var r)
                && TryParseByte(hex.Slice(4, 2), out var g)
                && TryParseByte(hex.Slice(6, 2), out var b))
            {
                color = new DocColor(r, g, b, a);
                return true;
            }

            return false;
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
        var parts = content.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
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
        if (trimmed.EndsWith('%') && double.TryParse(trimmed.AsSpan(0, trimmed.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            percent = Math.Clamp(percent, 0d, 100d);
            value = (byte)Math.Round(percent * 2.55d, MidpointRounding.AwayFromZero);
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            number = Math.Clamp(number, 0d, 255d);
            value = (byte)Math.Round(number, MidpointRounding.AwayFromZero);
            return true;
        }

        return false;
    }

    private static bool TryParseAlpha(string text, out byte value)
    {
        value = 255;
        var trimmed = text.Trim();
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
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

        return false;
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
            nibble = 10 + (value - 'a');
            return true;
        }

        if (value >= 'A' && value <= 'F')
        {
            nibble = 10 + (value - 'A');
            return true;
        }

        return false;
    }

    private static bool TryParseByte(ReadOnlySpan<char> hex, out byte value)
    {
        value = 0;
        if (hex.Length != 2)
        {
            return false;
        }

        if (!TryParseNibble(hex[0], out var high) || !TryParseNibble(hex[1], out var low))
        {
            return false;
        }

        value = (byte)((high << 4) + low);
        return true;
    }
}

using System.Globalization;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Editing;

public readonly record struct InkStrokeData(DocColor Color, float Thickness, IReadOnlyList<DocPoint> Points);

public static class InkStrokeParser
{
    public static bool TryParse(ImageInline image, out InkStrokeData stroke)
    {
        if (image is null)
        {
            stroke = default;
            return false;
        }

        return TryParse(image.Data, out stroke);
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out InkStrokeData stroke)
    {
        stroke = default;
        if (data.IsEmpty)
        {
            return false;
        }

        var svg = Encoding.UTF8.GetString(data);
        if (!TryReadAttribute(svg, "d", out var path))
        {
            return false;
        }

        var points = ParsePathPoints(path);
        if (points.Count < 2)
        {
            return false;
        }

        var color = DocColor.Black;
        if (TryReadAttribute(svg, "stroke", out var strokeValue) && TryParseColor(strokeValue, out var parsedColor))
        {
            color = parsedColor;
        }

        var thickness = 1f;
        if (TryReadAttribute(svg, "stroke-width", out var widthValue)
            && float.TryParse(widthValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth))
        {
            thickness = parsedWidth;
        }

        if (TryReadAttribute(svg, "stroke-opacity", out var opacityValue)
            && float.TryParse(opacityValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity))
        {
            var alpha = (byte)Math.Clamp((int)MathF.Round(opacity * 255f), 0, 255);
            color = new DocColor(color.R, color.G, color.B, alpha);
        }

        stroke = new InkStrokeData(color, thickness, points);
        return true;
    }

    private static bool TryReadAttribute(string svg, string name, out string value)
    {
        var marker = $"{name}=\"";
        var start = svg.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            value = string.Empty;
            return false;
        }

        start += marker.Length;
        var end = svg.IndexOf('"', start);
        if (end < 0)
        {
            value = string.Empty;
            return false;
        }

        value = svg.Substring(start, end - start);
        return true;
    }

    private static List<DocPoint> ParsePathPoints(string path)
    {
        var points = new List<DocPoint>(64);
        var span = path.AsSpan();
        var index = 0;

        while (index < span.Length)
        {
            SkipSeparators(span, ref index);
            if (index >= span.Length)
            {
                break;
            }

            var command = span[index];
            if (command is 'M' or 'L' or 'm' or 'l')
            {
                index++;
                continue;
            }

            if (!TryParseFloat(span, ref index, out var x))
            {
                break;
            }

            if (!TryParseFloat(span, ref index, out var y))
            {
                break;
            }

            points.Add(new DocPoint(x, y));
        }

        return points;
    }

    private static void SkipSeparators(ReadOnlySpan<char> span, ref int index)
    {
        while (index < span.Length)
        {
            var c = span[index];
            if (!char.IsWhiteSpace(c) && c != ',')
            {
                return;
            }

            index++;
        }
    }

    private static bool TryParseFloat(ReadOnlySpan<char> span, ref int index, out float value)
    {
        SkipSeparators(span, ref index);
        if (index >= span.Length)
        {
            value = 0f;
            return false;
        }

        var start = index;
        while (index < span.Length)
        {
            var c = span[index];
            if (char.IsWhiteSpace(c) || c == ',' || c is 'M' or 'L' or 'm' or 'l')
            {
                break;
            }

            index++;
        }

        if (start == index)
        {
            value = 0f;
            return false;
        }

        return float.TryParse(span.Slice(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseColor(string value, out DocColor color)
    {
        color = DocColor.Black;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            var hex = trimmed.AsSpan(1);
            if (hex.Length == 6
                && byte.TryParse(hex.Slice(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                && byte.TryParse(hex.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                && byte.TryParse(hex.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                color = new DocColor(r, g, b);
                return true;
            }

            return false;
        }

        if (trimmed.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            var span = trimmed.AsSpan(4, trimmed.Length - 5);
            var first = span.IndexOf(',');
            if (first <= 0)
            {
                return false;
            }

            var second = span.Slice(first + 1).IndexOf(',');
            if (second <= 0)
            {
                return false;
            }

            if (!byte.TryParse(span[..first], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
            {
                return false;
            }

            var secondStart = first + 1;
            if (!byte.TryParse(span.Slice(secondStart, second), NumberStyles.Integer, CultureInfo.InvariantCulture, out var g))
            {
                return false;
            }

            var bSpan = span.Slice(secondStart + second + 1);
            if (!byte.TryParse(bSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                return false;
            }

            color = new DocColor(r, g, b);
            return true;
        }

        return false;
    }
}

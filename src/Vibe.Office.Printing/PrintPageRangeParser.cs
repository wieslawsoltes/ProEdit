using System.Globalization;

namespace Vibe.Office.Printing;

public static class PrintPageRangeParser
{
    public static bool TryParse(string? value, out IReadOnlyList<PrintPageRange> ranges)
    {
        ranges = Array.Empty<PrintPageRange>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var result = new List<PrintPageRange>();
        foreach (var token in tokens)
        {
            if (TryParseToken(token, out var range))
            {
                result.Add(range);
                continue;
            }

            return false;
        }

        ranges = Normalize(result);
        return ranges.Count > 0;
    }

    public static IReadOnlyList<PrintPageRange> Normalize(IReadOnlyList<PrintPageRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return Array.Empty<PrintPageRange>();
        }

        var ordered = ranges.OrderBy(r => r.Start).ThenBy(r => r.End).ToList();
        var merged = new List<PrintPageRange> { ordered[0] };

        for (var i = 1; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var last = merged[^1];
            if (current.Start <= last.End + 1)
            {
                merged[^1] = new PrintPageRange(last.Start, Math.Max(last.End, current.End));
                continue;
            }

            merged.Add(current);
        }

        return merged;
    }

    public static string ToDisplayString(IReadOnlyList<PrintPageRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return string.Empty;
        }

        var builder = new List<string>(ranges.Count);
        foreach (var range in Normalize(ranges))
        {
            builder.Add(range.Start == range.End
                ? range.Start.ToString(CultureInfo.InvariantCulture)
                : $"{range.Start}-{range.End}");
        }

        return string.Join(", ", builder);
    }

    private static bool TryParseToken(string token, out PrintPageRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            if (!TryParsePageNumber(parts[0], out var page))
            {
                return false;
            }

            range = new PrintPageRange(page, page);
            return true;
        }

        if (parts.Length == 2)
        {
            if (!TryParsePageNumber(parts[0], out var start) || !TryParsePageNumber(parts[1], out var end))
            {
                return false;
            }

            if (end < start)
            {
                (start, end) = (end, start);
            }

            range = new PrintPageRange(start, end);
            return true;
        }

        return false;
    }

    private static bool TryParsePageNumber(string value, out int page)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
        {
            page = parsed;
            return true;
        }

        page = 0;
        return false;
    }
}

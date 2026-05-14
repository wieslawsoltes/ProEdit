namespace ProEdit.Layout;

internal enum EastAsianWidth
{
    Neutral,
    Ambiguous,
    Halfwidth,
    Fullwidth,
    Narrow,
    Wide
}

internal static class TextEastAsianWidth
{
    public static EastAsianWidth GetWidth(int codepoint)
    {
        var ranges = TextEastAsianWidthData.EastAsianWidthRanges;
        var lo = 0;
        var hi = ranges.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var range = ranges[mid];
            if (codepoint < range.Start)
            {
                hi = mid - 1;
            }
            else if (codepoint > range.End)
            {
                lo = mid + 1;
            }
            else
            {
                return range.Width;
            }
        }

        return EastAsianWidth.Neutral;
    }

    public static bool IsFullWideOrHalf(int codepoint)
    {
        var width = GetWidth(codepoint);
        return width == EastAsianWidth.Fullwidth
               || width == EastAsianWidth.Wide
               || width == EastAsianWidth.Halfwidth;
    }
}

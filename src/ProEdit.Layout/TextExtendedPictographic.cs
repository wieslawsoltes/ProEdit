namespace ProEdit.Layout;

internal static class TextExtendedPictographic
{
    public static bool IsExtendedPictographic(int codepoint)
    {
        var ranges = TextExtendedPictographicData.ExtendedPictographicRanges;
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
                return true;
            }
        }

        return false;
    }
}

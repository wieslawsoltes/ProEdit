namespace Vibe.Office.Layout;

internal static class GreedyLineBreaker
{
    public static IEnumerable<ParagraphLineBreak> BreakParagraph(
        string text,
        float firstLineWidth,
        float otherLineWidth,
        Func<int, int, float> measureWidth)
    {
        ArgumentNullException.ThrowIfNull(measureWidth);

        var start = 0;
        var isFirstLine = true;
        while (start < text.Length)
        {
            var maxWidth = isFirstLine ? firstLineWidth : otherLineWidth;
            var length = 0;
            var lastBreak = -1;
            for (var i = start; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == ' ')
                {
                    lastBreak = i;
                }

                var width = measureWidth(start, i - start + 1);
                if (width > maxWidth && i > start)
                {
                    length = lastBreak >= start ? lastBreak - start : i - start;
                    break;
                }

                length = i - start + 1;
            }

            if (length <= 0)
            {
                length = Math.Min(1, text.Length - start);
            }

            yield return new ParagraphLineBreak(start, length, false, null, 0f);

            start += length;
            isFirstLine = false;
            while (start < text.Length && text[start] == ' ')
            {
                start++;
            }
        }
    }
}

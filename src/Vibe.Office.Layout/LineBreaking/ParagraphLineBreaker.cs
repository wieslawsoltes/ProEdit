namespace Vibe.Office.Layout;

internal static class ParagraphLineBreaker
{
    public static IEnumerable<ParagraphLineBreak> BreakParagraph(
        string text,
        IReadOnlyList<InlineSpan> spans,
        float firstLineWidth,
        float otherLineWidth,
        ITextMeasurer measurer,
        Func<int, int, float> measureWidth)
    {
        ArgumentNullException.ThrowIfNull(measureWidth);

        if (KnuthPlassLineBreaker.TryBreakParagraph(text, spans, firstLineWidth, otherLineWidth, measurer, out var breaks))
        {
            return breaks;
        }

        return GreedyLineBreaker.BreakParagraph(text, firstLineWidth, otherLineWidth, measureWidth);
    }
}

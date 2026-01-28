namespace Vibe.Office.Layout;

internal static class ParagraphLineBreaker
{
    public static IEnumerable<ParagraphLineBreak> BreakParagraph(
        string text,
        IReadOnlyList<InlineSpan> spans,
        float firstLineWidth,
        float otherLineWidth,
        ITextMeasurer measurer,
        float charGridSpacing,
        LineBreakOptions options,
        Func<int, int, float> measureFirstLineWidth,
        Func<int, int, float> measureOtherLineWidth)
    {
        ArgumentNullException.ThrowIfNull(measureFirstLineWidth);
        ArgumentNullException.ThrowIfNull(measureOtherLineWidth);

        if (!options.UseWord97LineBreakRules
            && KnuthPlassLineBreaker.TryBreakParagraph(text, spans, firstLineWidth, otherLineWidth, measurer, charGridSpacing, options, out var breaks))
        {
            return breaks;
        }

        return GreedyLineBreaker.BreakParagraph(text, firstLineWidth, otherLineWidth, options, measureFirstLineWidth, measureOtherLineWidth);
    }
}

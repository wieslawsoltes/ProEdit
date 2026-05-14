namespace ProEdit.Layout;

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

        if (!options.UseWord97LineBreakRules)
        {
            if (KnuthPlassLineBreaker.TryBreakParagraph(text, spans, firstLineWidth, otherLineWidth, measurer, charGridSpacing, options, out var breaks))
            {
                return breaks;
            }

            return Uax14LineBreaker.BreakParagraph(text, firstLineWidth, otherLineWidth, options, measureFirstLineWidth, measureOtherLineWidth);
        }

        return GreedyLineBreaker.BreakParagraph(text, firstLineWidth, otherLineWidth, options, measureFirstLineWidth, measureOtherLineWidth);
    }
}

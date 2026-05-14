using System.Globalization;
using System.Text;

namespace ProEdit.Layout;

internal static class GreedyLineBreaker
{
    public static IEnumerable<ParagraphLineBreak> BreakParagraph(
        string text,
        float firstLineWidth,
        float otherLineWidth,
        LineBreakOptions options,
        Func<int, int, float> measureFirstLineWidth,
        Func<int, int, float> measureOtherLineWidth)
    {
        ArgumentNullException.ThrowIfNull(measureFirstLineWidth);
        ArgumentNullException.ThrowIfNull(measureOtherLineWidth);

        var useSimpleBreaks = !options.UseAltKinsokuLineBreakRules && !options.DoNotWrapTextWithPunctuation;
        var start = 0;
        var isFirstLine = true;
        while (start < text.Length)
        {
            var maxWidth = isFirstLine ? firstLineWidth : otherLineWidth;
            var length = 0;
            var lastBreak = -1;
            if (useSimpleBreaks)
            {
                for (var i = start; i < text.Length; i++)
                {
                    var ch = text[i];
                    if (ch == ' ')
                    {
                        lastBreak = i;
                    }

                    var width = isFirstLine
                        ? measureFirstLineWidth(start, i - start + 1)
                        : measureOtherLineWidth(start, i - start + 1);
                    if (width > maxWidth && i > start)
                    {
                        length = lastBreak >= start ? lastBreak - start : i - start;
                        break;
                    }

                    length = i - start + 1;
                }
            }
            else
            {
                var lastAllowedNonSpaceBreak = -1;
                for (var i = start; i < text.Length;)
                {
                    var clusterLength = TextCluster.GetNextClusterLength(text.AsSpan(), i);
                    var ch = text[i];
                    if (ch == ' ')
                    {
                        lastBreak = i;
                    }

                    var segmentLength = i - start + clusterLength;
                    var width = isFirstLine
                        ? measureFirstLineWidth(start, segmentLength)
                        : measureOtherLineWidth(start, segmentLength);
                    if (width > maxWidth && i > start)
                    {
                        if (lastBreak >= start)
                        {
                            length = lastBreak - start;
                        }
                        else if (lastAllowedNonSpaceBreak > start)
                        {
                            length = lastAllowedNonSpaceBreak - start;
                        }
                        else
                        {
                            length = i - start;
                        }

                        break;
                    }

                    length = segmentLength;
                    var nextIndex = i + clusterLength;
                    if (nextIndex < text.Length && lastBreak < start)
                    {
                        if (ShouldAllowCjkBreak(text, i, clusterLength, nextIndex, options))
                        {
                            lastAllowedNonSpaceBreak = nextIndex;
                        }
                    }

                    i = nextIndex;
                }
            }

            if (length <= 0)
            {
                length = Math.Min(1, text.Length - start);
            }

            yield return new ParagraphLineBreak(start, length, false, null, 0f);

            start += length;
            isFirstLine = false;
            if (!options.WrapTrailingSpaces)
            {
                while (start < text.Length && text[start] == ' ')
                {
                    start++;
                }
            }
        }
    }

    private static bool ShouldAllowCjkBreak(
        string text,
        int currentIndex,
        int currentLength,
        int nextIndex,
        LineBreakOptions options)
    {
        var currentRune = GetClusterRune(text.AsSpan(currentIndex, currentLength));
        var nextLength = TextCluster.GetNextClusterLength(text.AsSpan(), nextIndex);
        var nextRune = GetClusterRune(text.AsSpan(nextIndex, nextLength));
        if (!TextScript.IsEastAsianRune(currentRune) && !TextScript.IsEastAsianRune(nextRune))
        {
            return false;
        }

        if (options.DoNotWrapTextWithPunctuation)
        {
            if (IsPunctuationOrSymbol(currentRune) || IsPunctuationOrSymbol(nextRune))
            {
                return false;
            }
        }

        if (options.UseAltKinsokuLineBreakRules)
        {
            if (IsOpeningPunctuation(currentRune) || IsClosingPunctuation(nextRune))
            {
                return false;
            }
        }

        return true;
    }

    private static Rune GetClusterRune(ReadOnlySpan<char> span)
    {
        if (Utf16Decoder.TryDecodeFromUtf16(span, out var rune, out _))
        {
            return rune;
        }

        return Rune.ReplacementChar;
    }

    private static bool IsOpeningPunctuation(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category == UnicodeCategory.OpenPunctuation
               || category == UnicodeCategory.InitialQuotePunctuation;
    }

    private static bool IsClosingPunctuation(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category == UnicodeCategory.ClosePunctuation
               || category == UnicodeCategory.FinalQuotePunctuation;
    }

    private static bool IsPunctuationOrSymbol(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category == UnicodeCategory.ConnectorPunctuation
               || category == UnicodeCategory.DashPunctuation
               || category == UnicodeCategory.OpenPunctuation
               || category == UnicodeCategory.ClosePunctuation
               || category == UnicodeCategory.InitialQuotePunctuation
               || category == UnicodeCategory.FinalQuotePunctuation
               || category == UnicodeCategory.OtherPunctuation
               || category == UnicodeCategory.MathSymbol
               || category == UnicodeCategory.CurrencySymbol
               || category == UnicodeCategory.ModifierSymbol
               || category == UnicodeCategory.OtherSymbol;
    }
}

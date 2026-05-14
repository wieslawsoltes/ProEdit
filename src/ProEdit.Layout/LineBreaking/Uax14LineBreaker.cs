using System.Buffers;
using System.Globalization;
using System.Text;

namespace ProEdit.Layout;

internal static class Uax14LineBreaker
{
    private enum BreakKind
    {
        Prohibited,
        Allowed,
        Mandatory
    }

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

        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<ParagraphLineBreak>();
        }

        var clusterCount = CountClusters(text.AsSpan());
        if (clusterCount <= 0)
        {
            return Array.Empty<ParagraphLineBreak>();
        }

        var intPool = ArrayPool<int>.Shared;
        var classPool = ArrayPool<LineBreakClass>.Shared;
        var boolPool = ArrayPool<bool>.Shared;
        var scriptPool = ArrayPool<TextScriptKind>.Shared;
        var breakPool = ArrayPool<BreakKind>.Shared;

        var starts = intPool.Rent(clusterCount + 1);
        var lengths = intPool.Rent(clusterCount);
        var codepoints = intPool.Rent(clusterCount);
        var classes = classPool.Rent(clusterCount);
        var scripts = scriptPool.Rent(clusterCount);
        var isInitialQuote = boolPool.Rent(clusterCount);
        var isFinalQuote = boolPool.Rent(clusterCount);
        var isExtendedPictographic = boolPool.Rent(clusterCount);
        var isEastAsianWidthFwh = boolPool.Rent(clusterCount);
        var prevNonSpace = intPool.Rent(clusterCount);
        var nextNonSpace = intPool.Rent(clusterCount);
        var prevNonNeutral = intPool.Rent(clusterCount);
        var nextNonNeutral = intPool.Rent(clusterCount);
        var riCountBefore = intPool.Rent(clusterCount + 1);
        var breakKinds = breakPool.Rent(clusterCount + 1);

        try
        {
            FillClusterData(text.AsSpan(), options, starts, lengths, codepoints, classes, scripts,
                isInitialQuote, isFinalQuote, isExtendedPictographic, isEastAsianWidthFwh);

            ResolveCombiningClasses(classes, clusterCount);
            FillNonSpaceIndexes(classes, clusterCount, prevNonSpace, nextNonSpace);
            FillNonNeutralIndexes(scripts, clusterCount, prevNonNeutral, nextNonNeutral);
            FillRiCounts(classes, clusterCount, riCountBefore);
            ComputeBreakKinds(clusterCount, classes, isInitialQuote, isFinalQuote, isExtendedPictographic, isEastAsianWidthFwh,
                prevNonSpace, nextNonSpace, riCountBefore, breakKinds);

            if (options.UseEastAsianBreakRules
                && (options.UseAltKinsokuLineBreakRules || options.DoNotWrapTextWithPunctuation))
            {
                ApplyKinsokuRestrictions(clusterCount, codepoints, classes, scripts, prevNonSpace, nextNonSpace,
                    prevNonNeutral, nextNonNeutral, breakKinds, options);
            }

            var lines = new List<ParagraphLineBreak>();
            var lineStartBoundary = 0;
            var lineStartIndex = starts[lineStartBoundary];
            var isFirstLine = true;

            while (lineStartBoundary < clusterCount)
            {
                var maxWidth = isFirstLine ? firstLineWidth : otherLineWidth;
                var lastAllowedBoundary = -1;
                var mandatoryBoundary = -1;
                var boundary = lineStartBoundary;
                var widthExceeded = false;

                for (var i = lineStartBoundary; i < clusterCount; i++)
                {
                    boundary = i + 1;
                    var breakKind = breakKinds[boundary];
                    if (breakKind == BreakKind.Mandatory)
                    {
                        mandatoryBoundary = boundary;
                        break;
                    }

                    if (breakKind == BreakKind.Allowed)
                    {
                        lastAllowedBoundary = boundary;
                    }

                    var length = starts[boundary] - lineStartIndex;
                    var width = isFirstLine
                        ? measureFirstLineWidth(lineStartIndex, length)
                        : measureOtherLineWidth(lineStartIndex, length);

                    if (width > maxWidth && boundary > lineStartBoundary)
                    {
                        widthExceeded = true;
                        break;
                    }
                }

                var breakBoundary = mandatoryBoundary;
                if (breakBoundary < 0)
                {
                    if (widthExceeded)
                    {
                        breakBoundary = lastAllowedBoundary > lineStartBoundary ? lastAllowedBoundary : Math.Min(lineStartBoundary + 1, clusterCount);
                    }
                    else if (boundary >= clusterCount)
                    {
                        breakBoundary = clusterCount;
                    }
                    else
                    {
                        breakBoundary = lastAllowedBoundary > lineStartBoundary ? lastAllowedBoundary : Math.Min(lineStartBoundary + 1, clusterCount);
                    }
                }

                var lineEndIndex = starts[breakBoundary];
                if (breakBoundary > lineStartBoundary && breakKinds[breakBoundary] == BreakKind.Mandatory)
                {
                    var breakClass = classes[breakBoundary - 1];
                    if (IsHardBreak(breakClass))
                    {
                        var hardBreakIndex = breakBoundary - 1;
                        if (breakClass == LineBreakClass.LF && hardBreakIndex > 0 && classes[hardBreakIndex - 1] == LineBreakClass.CR)
                        {
                            lineEndIndex = starts[hardBreakIndex - 1];
                        }
                        else
                        {
                            lineEndIndex = starts[hardBreakIndex];
                        }
                    }
                }

                var lineLength = Math.Max(0, lineEndIndex - lineStartIndex);
                lines.Add(new ParagraphLineBreak(lineStartIndex, lineLength, false, null, 0f));

                lineStartBoundary = breakBoundary;
                lineStartIndex = starts[lineStartBoundary];
                while (lineStartBoundary < clusterCount && IsHardBreak(classes[lineStartBoundary]))
                {
                    lineStartBoundary++;
                    lineStartIndex = starts[lineStartBoundary];
                }

                if (!options.WrapTrailingSpaces)
                {
                    while (lineStartBoundary < clusterCount && classes[lineStartBoundary] == LineBreakClass.SP)
                    {
                        lineStartBoundary++;
                        lineStartIndex = starts[lineStartBoundary];
                    }
                }

                isFirstLine = false;
            }

            return lines;
        }
        finally
        {
            intPool.Return(starts);
            intPool.Return(lengths);
            intPool.Return(codepoints);
            classPool.Return(classes);
            scriptPool.Return(scripts);
            boolPool.Return(isInitialQuote);
            boolPool.Return(isFinalQuote);
            boolPool.Return(isExtendedPictographic);
            boolPool.Return(isEastAsianWidthFwh);
            intPool.Return(prevNonSpace);
            intPool.Return(nextNonSpace);
            intPool.Return(prevNonNeutral);
            intPool.Return(nextNonNeutral);
            intPool.Return(riCountBefore);
            breakPool.Return(breakKinds);
        }
    }

    private static int CountClusters(ReadOnlySpan<char> text)
    {
        var count = 0;
        var index = 0;
        while (index < text.Length)
        {
            var length = TextCluster.GetNextClusterLength(text, index);
            if (length <= 0)
            {
                length = 1;
            }

            count++;
            index += length;
        }

        return count;
    }

    private static void FillClusterData(
        ReadOnlySpan<char> text,
        LineBreakOptions options,
        int[] starts,
        int[] lengths,
        int[] codepoints,
        LineBreakClass[] classes,
        TextScriptKind[] scripts,
        bool[] isInitialQuote,
        bool[] isFinalQuote,
        bool[] isExtendedPictographic,
        bool[] isEastAsianWidthFwh)
    {
        var index = 0;
        var clusterIndex = 0;
        while (index < text.Length)
        {
            var length = TextCluster.GetNextClusterLength(text, index);
            if (length <= 0)
            {
                length = 1;
            }

            starts[clusterIndex] = index;
            lengths[clusterIndex] = length;
            var cluster = text.Slice(index, length);
            var (codepoint, lineClass, category) = GetClusterInfo(cluster);
            lineClass = TextLineBreak.ResolveLineBreakClass(lineClass, category);
            if (!options.UseEastAsianBreakRules && IsEastAsianLineBreakClass(lineClass))
            {
                lineClass = LineBreakClass.AL;
            }

            classes[clusterIndex] = lineClass;
            codepoints[clusterIndex] = codepoint;
            scripts[clusterIndex] = GetScriptKind(codepoint);
            isInitialQuote[clusterIndex] = lineClass == LineBreakClass.QU && category == UnicodeCategory.InitialQuotePunctuation;
            isFinalQuote[clusterIndex] = lineClass == LineBreakClass.QU && category == UnicodeCategory.FinalQuotePunctuation;
            isExtendedPictographic[clusterIndex] = TextExtendedPictographic.IsExtendedPictographic(codepoint);
            isEastAsianWidthFwh[clusterIndex] = TextEastAsianWidth.IsFullWideOrHalf(codepoint);

            index += length;
            clusterIndex++;
        }

        starts[clusterIndex] = text.Length;
    }

    private static (int Codepoint, LineBreakClass Class, UnicodeCategory Category) GetClusterInfo(ReadOnlySpan<char> cluster)
    {
        var offset = 0;
        var firstCodepoint = -1;
        var firstClass = LineBreakClass.XX;
        var firstCategory = UnicodeCategory.OtherNotAssigned;
        while (offset < cluster.Length)
        {
            if (!Utf16Decoder.TryDecodeFromUtf16(cluster[offset..], out var rune, out var consumed))
            {
                rune = new Rune(cluster[offset]);
                consumed = 1;
            }

            if (firstCodepoint < 0)
            {
                firstCodepoint = rune.Value;
                firstClass = TextLineBreak.GetLineBreakClass(firstCodepoint);
                firstCategory = Rune.GetUnicodeCategory(rune);
            }

            var lineClass = TextLineBreak.GetLineBreakClass(rune.Value);
            if (lineClass != LineBreakClass.CM && lineClass != LineBreakClass.ZWJ)
            {
                return (rune.Value, lineClass, Rune.GetUnicodeCategory(rune));
            }

            offset += consumed;
        }

        if (firstCodepoint < 0)
        {
            return (Rune.ReplacementChar.Value, LineBreakClass.XX, UnicodeCategory.OtherNotAssigned);
        }

        return (firstCodepoint, firstClass, firstCategory);
    }

    private static TextScriptKind GetScriptKind(int codepoint)
    {
        if (Rune.TryCreate(codepoint, out var rune))
        {
            return TextScript.ClassifyRune(rune);
        }

        return TextScriptKind.Neutral;
    }

    private static void ResolveCombiningClasses(LineBreakClass[] classes, int count)
    {
        var hasPrevious = false;
        var previous = LineBreakClass.AL;
        for (var i = 0; i < count; i++)
        {
            var klass = classes[i];
            if (klass == LineBreakClass.CM || klass == LineBreakClass.ZWJ)
            {
                classes[i] = hasPrevious ? previous : LineBreakClass.AL;
            }
            else
            {
                previous = klass;
                hasPrevious = true;
            }
        }
    }

    private static void FillNonSpaceIndexes(LineBreakClass[] classes, int count, int[] prevNonSpace, int[] nextNonSpace)
    {
        var last = -1;
        for (var i = 0; i < count; i++)
        {
            if (classes[i] != LineBreakClass.SP)
            {
                last = i;
            }

            prevNonSpace[i] = last;
        }

        var next = -1;
        for (var i = count - 1; i >= 0; i--)
        {
            if (classes[i] != LineBreakClass.SP)
            {
                next = i;
            }

            nextNonSpace[i] = next;
        }
    }

    private static void FillNonNeutralIndexes(TextScriptKind[] scripts, int count, int[] prevNonNeutral, int[] nextNonNeutral)
    {
        var last = -1;
        for (var i = 0; i < count; i++)
        {
            if (scripts[i] != TextScriptKind.Neutral)
            {
                last = i;
            }

            prevNonNeutral[i] = last;
        }

        var next = -1;
        for (var i = count - 1; i >= 0; i--)
        {
            if (scripts[i] != TextScriptKind.Neutral)
            {
                next = i;
            }

            nextNonNeutral[i] = next;
        }
    }

    private static void FillRiCounts(LineBreakClass[] classes, int count, int[] riCountBefore)
    {
        var consecutive = 0;
        for (var i = 0; i <= count; i++)
        {
            riCountBefore[i] = consecutive;
            if (i < count && classes[i] == LineBreakClass.RI)
            {
                consecutive++;
            }
            else
            {
                consecutive = 0;
            }
        }
    }

    private static void ComputeBreakKinds(
        int count,
        LineBreakClass[] classes,
        bool[] isInitialQuote,
        bool[] isFinalQuote,
        bool[] isExtendedPictographic,
        bool[] isEastAsianWidthFwh,
        int[] prevNonSpace,
        int[] nextNonSpace,
        int[] riCountBefore,
        BreakKind[] breakKinds)
    {
        for (var boundary = 0; boundary <= count; boundary++)
        {
            if (boundary == 0)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (boundary == count)
            {
                breakKinds[boundary] = BreakKind.Mandatory;
                continue;
            }

            var leftIndex = boundary - 1;
            var rightIndex = boundary;
            var leftClass = classes[leftIndex];
            var rightClass = classes[rightIndex];
            var prevNonSpaceIndex = leftIndex >= 0 ? prevNonSpace[leftIndex] : -1;
            var nextNonSpaceIndex = rightIndex < count ? nextNonSpace[rightIndex] : -1;
            var prevNonSpaceClass = prevNonSpaceIndex >= 0 ? classes[prevNonSpaceIndex] : LineBreakClass.XX;
            var nextNonSpaceClass = nextNonSpaceIndex >= 0 ? classes[nextNonSpaceIndex] : LineBreakClass.XX;

            // LB4
            if (leftClass == LineBreakClass.BK)
            {
                breakKinds[boundary] = BreakKind.Mandatory;
                continue;
            }

            // LB5
            if (leftClass == LineBreakClass.CR && rightClass == LineBreakClass.LF)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.CR || leftClass == LineBreakClass.LF || leftClass == LineBreakClass.NL)
            {
                breakKinds[boundary] = BreakKind.Mandatory;
                continue;
            }

            // LB6
            if (rightClass == LineBreakClass.BK || rightClass == LineBreakClass.CR
                || rightClass == LineBreakClass.LF || rightClass == LineBreakClass.NL)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB7
            if (rightClass == LineBreakClass.SP || rightClass == LineBreakClass.ZW)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB8
            if (rightClass != LineBreakClass.SP && prevNonSpaceClass == LineBreakClass.ZW)
            {
                breakKinds[boundary] = BreakKind.Allowed;
                continue;
            }

            // LB8a
            if (leftClass == LineBreakClass.ZWJ)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB11
            if (leftClass == LineBreakClass.WJ || rightClass == LineBreakClass.WJ)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB12
            if (leftClass == LineBreakClass.GL)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB12a
            if (rightClass == LineBreakClass.GL
                && leftClass != LineBreakClass.SP
                && leftClass != LineBreakClass.BA
                && leftClass != LineBreakClass.HY)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB13
            if (rightClass == LineBreakClass.CL || rightClass == LineBreakClass.CP || rightClass == LineBreakClass.EX
                || rightClass == LineBreakClass.IS || rightClass == LineBreakClass.SY)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB14
            if (rightClass != LineBreakClass.SP && prevNonSpaceClass == LineBreakClass.OP)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB15a
            if (rightClass != LineBreakClass.SP && prevNonSpaceIndex >= 0
                && classes[prevNonSpaceIndex] == LineBreakClass.QU
                && isInitialQuote[prevNonSpaceIndex])
            {
                var beforeQuoteIndex = prevNonSpaceIndex - 1;
                if (beforeQuoteIndex < 0)
                {
                    breakKinds[boundary] = BreakKind.Prohibited;
                    continue;
                }

                var beforeQuoteClass = classes[beforeQuoteIndex];
                if (beforeQuoteClass == LineBreakClass.BK || beforeQuoteClass == LineBreakClass.CR || beforeQuoteClass == LineBreakClass.LF
                    || beforeQuoteClass == LineBreakClass.NL || beforeQuoteClass == LineBreakClass.OP || beforeQuoteClass == LineBreakClass.QU
                    || beforeQuoteClass == LineBreakClass.GL || beforeQuoteClass == LineBreakClass.SP || beforeQuoteClass == LineBreakClass.ZW)
                {
                    breakKinds[boundary] = BreakKind.Prohibited;
                    continue;
                }
            }

            // LB15b
            if (rightClass == LineBreakClass.QU && isFinalQuote[rightIndex])
            {
                var nextClass = rightIndex + 1 < count ? classes[rightIndex + 1] : LineBreakClass.XX;
                if (nextClass == LineBreakClass.SP || nextClass == LineBreakClass.GL || nextClass == LineBreakClass.WJ
                    || nextClass == LineBreakClass.CL || nextClass == LineBreakClass.QU || nextClass == LineBreakClass.CP
                    || nextClass == LineBreakClass.EX || nextClass == LineBreakClass.IS || nextClass == LineBreakClass.SY
                    || nextClass == LineBreakClass.BK || nextClass == LineBreakClass.CR || nextClass == LineBreakClass.LF
                    || nextClass == LineBreakClass.NL || nextClass == LineBreakClass.ZW || rightIndex + 1 >= count)
                {
                    breakKinds[boundary] = BreakKind.Prohibited;
                    continue;
                }
            }

            // LB16
            if (rightClass != LineBreakClass.SP && (prevNonSpaceClass == LineBreakClass.CL || prevNonSpaceClass == LineBreakClass.CP)
                && nextNonSpaceClass == LineBreakClass.NS)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB17
            if (rightClass != LineBreakClass.SP && prevNonSpaceClass == LineBreakClass.B2 && nextNonSpaceClass == LineBreakClass.B2)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB18
            if (leftClass == LineBreakClass.SP)
            {
                breakKinds[boundary] = BreakKind.Allowed;
                continue;
            }

            // LB19
            if (leftClass == LineBreakClass.QU || rightClass == LineBreakClass.QU)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB20
            if (leftClass == LineBreakClass.CB || rightClass == LineBreakClass.CB)
            {
                breakKinds[boundary] = BreakKind.Allowed;
                continue;
            }

            // LB21
            if (rightClass == LineBreakClass.BA || rightClass == LineBreakClass.HY || rightClass == LineBreakClass.NS)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.BB)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB21a
            if (leftClass == LineBreakClass.HL && (rightClass == LineBreakClass.HY || rightClass == LineBreakClass.BA))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB21b
            if (leftClass == LineBreakClass.SY && rightClass == LineBreakClass.HL)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB22
            if (rightClass == LineBreakClass.IN)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB23
            if ((leftClass == LineBreakClass.AL || leftClass == LineBreakClass.HL) && rightClass == LineBreakClass.NU)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.NU && (rightClass == LineBreakClass.AL || rightClass == LineBreakClass.HL))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB23a
            if (leftClass == LineBreakClass.PR && (rightClass == LineBreakClass.ID || rightClass == LineBreakClass.EB || rightClass == LineBreakClass.EM))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if ((leftClass == LineBreakClass.ID || leftClass == LineBreakClass.EB || leftClass == LineBreakClass.EM) && rightClass == LineBreakClass.PO)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB24
            if ((leftClass == LineBreakClass.PR || leftClass == LineBreakClass.PO)
                && (rightClass == LineBreakClass.AL || rightClass == LineBreakClass.HL))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if ((leftClass == LineBreakClass.AL || leftClass == LineBreakClass.HL)
                && (rightClass == LineBreakClass.PR || rightClass == LineBreakClass.PO))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB25
            if ((leftClass == LineBreakClass.CL || leftClass == LineBreakClass.CP) && (rightClass == LineBreakClass.PO || rightClass == LineBreakClass.PR))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.NU && (rightClass == LineBreakClass.PO || rightClass == LineBreakClass.PR))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if ((leftClass == LineBreakClass.PO || leftClass == LineBreakClass.PR) && (rightClass == LineBreakClass.OP || rightClass == LineBreakClass.NU))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.HY && rightClass == LineBreakClass.NU)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.IS && rightClass == LineBreakClass.NU)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.NU && rightClass == LineBreakClass.NU)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.SY && rightClass == LineBreakClass.NU)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB26
            if (leftClass == LineBreakClass.JL && (rightClass == LineBreakClass.JL || rightClass == LineBreakClass.JV
                                                   || rightClass == LineBreakClass.H2 || rightClass == LineBreakClass.H3))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if ((leftClass == LineBreakClass.JV || leftClass == LineBreakClass.H2)
                && (rightClass == LineBreakClass.JV || rightClass == LineBreakClass.JT))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if ((leftClass == LineBreakClass.JT || leftClass == LineBreakClass.H3) && rightClass == LineBreakClass.JT)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB27
            if ((leftClass == LineBreakClass.JL || leftClass == LineBreakClass.JV || leftClass == LineBreakClass.JT
                 || leftClass == LineBreakClass.H2 || leftClass == LineBreakClass.H3)
                && rightClass == LineBreakClass.PO)
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.PR && (rightClass == LineBreakClass.JL || rightClass == LineBreakClass.JV
                                                   || rightClass == LineBreakClass.JT || rightClass == LineBreakClass.H2
                                                   || rightClass == LineBreakClass.H3))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB28
            if ((leftClass == LineBreakClass.AL || leftClass == LineBreakClass.HL)
                && (rightClass == LineBreakClass.AL || rightClass == LineBreakClass.HL))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB28a
            if (leftClass == LineBreakClass.AP && IsIndicLinkClass(rightClass))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (IsIndicLinkClass(leftClass) && (rightClass == LineBreakClass.VF || rightClass == LineBreakClass.VI))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.VI && IsIndicVowelClass(rightClass))
            {
                var beforeViIndex = leftIndex - 1;
                if (beforeViIndex >= 0 && IsIndicLinkClass(classes[beforeViIndex]))
                {
                    breakKinds[boundary] = BreakKind.Prohibited;
                    continue;
                }
            }

            if (IsIndicLinkClass(leftClass) && IsIndicLinkClass(rightClass))
            {
                var nextIndex = rightIndex + 1;
                if (nextIndex < count && classes[nextIndex] == LineBreakClass.VF)
                {
                    breakKinds[boundary] = BreakKind.Prohibited;
                    continue;
                }
            }

            // LB29
            if (leftClass == LineBreakClass.IS && (rightClass == LineBreakClass.AL || rightClass == LineBreakClass.HL))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB30
            if ((leftClass == LineBreakClass.AL || leftClass == LineBreakClass.HL || leftClass == LineBreakClass.NU)
                && rightClass == LineBreakClass.OP
                && !isEastAsianWidthFwh[rightIndex])
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            if (leftClass == LineBreakClass.CP
                && !isEastAsianWidthFwh[leftIndex]
                && (rightClass == LineBreakClass.AL || rightClass == LineBreakClass.HL || rightClass == LineBreakClass.NU))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB30a
            if (leftClass == LineBreakClass.RI && rightClass == LineBreakClass.RI)
            {
                if (riCountBefore[boundary] % 2 == 1)
                {
                    breakKinds[boundary] = BreakKind.Prohibited;
                    continue;
                }
            }

            // LB30b
            if (rightClass == LineBreakClass.EM && (leftClass == LineBreakClass.EB || isExtendedPictographic[leftIndex]))
            {
                breakKinds[boundary] = BreakKind.Prohibited;
                continue;
            }

            // LB31
            breakKinds[boundary] = BreakKind.Allowed;
        }
    }

    private static void ApplyKinsokuRestrictions(
        int count,
        int[] codepoints,
        LineBreakClass[] classes,
        TextScriptKind[] scripts,
        int[] prevNonSpace,
        int[] nextNonSpace,
        int[] prevNonNeutral,
        int[] nextNonNeutral,
        BreakKind[] breakKinds,
        LineBreakOptions options)
    {
        for (var boundary = 1; boundary < count; boundary++)
        {
            if (breakKinds[boundary] != BreakKind.Allowed)
            {
                continue;
            }

            var leftIndex = boundary - 1;
            var rightIndex = boundary;
            if (classes[leftIndex] == LineBreakClass.CB || classes[rightIndex] == LineBreakClass.CB)
            {
                continue;
            }

            if (classes[rightIndex] == LineBreakClass.SP)
            {
                continue;
            }

            if (!IsEastAsianContext(boundary, prevNonNeutral, nextNonNeutral, scripts))
            {
                continue;
            }

            var prevIndex = prevNonSpace[leftIndex];
            var nextIndex = nextNonSpace[rightIndex];
            if (prevIndex < 0 || nextIndex < 0)
            {
                continue;
            }

            var leftRune = GetRune(codepoints[prevIndex]);
            var rightRune = GetRune(codepoints[nextIndex]);

            if (options.DoNotWrapTextWithPunctuation)
            {
                if (IsPunctuationOrSymbol(leftRune) || IsPunctuationOrSymbol(rightRune))
                {
                    breakKinds[boundary] = BreakKind.Prohibited;
                    continue;
                }
            }

            if (options.UseAltKinsokuLineBreakRules)
            {
                if (IsOpeningPunctuation(leftRune) || IsClosingPunctuation(rightRune))
                {
                    breakKinds[boundary] = BreakKind.Prohibited;
                }
            }
        }
    }

    private static bool IsEastAsianContext(
        int boundary,
        int[] prevNonNeutral,
        int[] nextNonNeutral,
        TextScriptKind[] scripts)
    {
        var leftIndex = boundary - 1;
        var rightIndex = boundary;
        var leftScriptIndex = leftIndex >= 0 ? prevNonNeutral[leftIndex] : -1;
        var rightScriptIndex = rightIndex < nextNonNeutral.Length ? nextNonNeutral[rightIndex] : -1;
        if (leftScriptIndex >= 0 && scripts[leftScriptIndex] == TextScriptKind.EastAsian)
        {
            return true;
        }

        if (rightScriptIndex >= 0 && scripts[rightScriptIndex] == TextScriptKind.EastAsian)
        {
            return true;
        }

        return false;
    }

    private static bool IsEastAsianLineBreakClass(LineBreakClass klass)
    {
        return klass == LineBreakClass.ID
               || klass == LineBreakClass.CJ
               || klass == LineBreakClass.JL
               || klass == LineBreakClass.JV
               || klass == LineBreakClass.JT
               || klass == LineBreakClass.H2
               || klass == LineBreakClass.H3;
    }

    private static bool IsIndicLinkClass(LineBreakClass klass)
    {
        return klass == LineBreakClass.AK || klass == LineBreakClass.AS || klass == LineBreakClass.CM;
    }

    private static bool IsIndicVowelClass(LineBreakClass klass)
    {
        return klass == LineBreakClass.AK || klass == LineBreakClass.CM;
    }

    private static bool IsHardBreak(LineBreakClass klass)
    {
        return klass == LineBreakClass.BK || klass == LineBreakClass.CR
               || klass == LineBreakClass.LF || klass == LineBreakClass.NL;
    }

    private static Rune GetRune(int codepoint)
    {
        return Rune.TryCreate(codepoint, out var rune) ? rune : Rune.ReplacementChar;
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

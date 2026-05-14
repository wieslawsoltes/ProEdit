using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace ProEdit.Layout;

public enum BidiDirection
{
    Ltr,
    Rtl
}

public readonly record struct BidiSpan(int Start, int Length, int Level);

internal enum BidiClass
{
    L,
    R,
    AL,
    EN,
    AN,
    ES,
    ET,
    CS,
    NSM,
    BN,
    ON,
    LRE,
    RLE,
    LRO,
    RLO,
    PDF,
    LRI,
    RLI,
    FSI,
    PDI,
    B,
    S,
    WS
}

public static class TextBidi
{
    private const byte MaxDepth = 125;
    private const int MaxBracketPairs = 63;
    private static readonly TextBidiData.BidiRange[] SortedBidiRanges = CreateSortedRanges();

    public static bool ResolveBaseIsRtl(ReadOnlySpan<char> text, bool? explicitRtl)
    {
        if (explicitRtl.HasValue)
        {
            return explicitRtl.Value;
        }

        return FindFirstStrongDirection(text) == BidiDirection.Rtl;
    }

    public static BidiDirection FindFirstStrongDirection(ReadOnlySpan<char> text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!Utf16Decoder.TryDecodeFromUtf16(text[index..], out var rune, out var consumed))
            {
                rune = new Rune(text[index]);
                consumed = 1;
            }

            var klass = GetBidiClass(rune.Value);
            if (klass == BidiClass.L)
            {
                return BidiDirection.Ltr;
            }

            if (klass == BidiClass.R || klass == BidiClass.AL)
            {
                return BidiDirection.Rtl;
            }

            index += consumed;
        }

        return BidiDirection.Ltr;
    }

    public static List<BidiSpan> GetBidiSpans(ReadOnlySpan<char> text, bool baseRtl)
    {
        var spans = new List<BidiSpan>();
        if (text.IsEmpty)
        {
            return spans;
        }

        var charLevels = ResolveLevels(text, baseRtl);
        if (charLevels.Length == 0)
        {
            return spans;
        }

        var spanStart = 0;
        var spanLevel = charLevels[0];
        for (var i = 1; i < charLevels.Length; i++)
        {
            if (charLevels[i] == spanLevel)
            {
                continue;
            }

            spans.Add(new BidiSpan(spanStart, i - spanStart, spanLevel));
            spanStart = i;
            spanLevel = charLevels[i];
        }

        spans.Add(new BidiSpan(spanStart, charLevels.Length - spanStart, spanLevel));
        return spans;
    }

    public static void ReorderByLevels<T>(List<T> items, Func<T, int> getLevel, int baseLevel)
    {
        if (items.Count <= 1)
        {
            return;
        }

        var maxLevel = baseLevel;
        for (var i = 0; i < items.Count; i++)
        {
            maxLevel = Math.Max(maxLevel, getLevel(items[i]));
        }

        for (var level = maxLevel; level > baseLevel; level--)
        {
            var index = 0;
            while (index < items.Count)
            {
                while (index < items.Count && getLevel(items[index]) < level)
                {
                    index++;
                }

                if (index >= items.Count)
                {
                    break;
                }

                var start = index;
                while (index < items.Count && getLevel(items[index]) >= level)
                {
                    index++;
                }

                var count = index - start;
                if (count > 1)
                {
                    items.Reverse(start, count);
                }
            }
        }
    }

    private static int[] ResolveLevels(ReadOnlySpan<char> text, bool baseRtl)
    {
        var textLength = text.Length;
        var charLevels = new int[textLength];
        if (textLength == 0)
        {
            return charLevels;
        }

        var maxCount = textLength;
        var codepoints = ArrayPool<int>.Shared.Rent(maxCount);
        var originalTypes = ArrayPool<BidiClass>.Shared.Rent(maxCount);
        var types = ArrayPool<BidiClass>.Shared.Rent(maxCount);
        var levels = ArrayPool<byte>.Shared.Rent(maxCount);
        var charStarts = ArrayPool<int>.Shared.Rent(maxCount);
        var charLengths = ArrayPool<byte>.Shared.Rent(maxCount);
        var removed = ArrayPool<bool>.Shared.Rent(maxCount);
        var matchingPdi = ArrayPool<int>.Shared.Rent(maxCount);
        var matchingIsolate = ArrayPool<int>.Shared.Rent(maxCount);
        var visibleIndices = ArrayPool<int>.Shared.Rent(maxCount);
        var visibleIndexMap = ArrayPool<int>.Shared.Rent(maxCount);
        var runIndexOfVisible = ArrayPool<int>.Shared.Rent(maxCount);
        var runs = ArrayPool<LevelRun>.Shared.Rent(maxCount);

        try
        {
            var count = DecodeText(text, codepoints, charStarts, charLengths, originalTypes, types);
            if (count == 0)
            {
                return charLevels;
            }

            Array.Fill(removed, false, 0, count);
            Array.Fill(matchingPdi, -1, 0, count);
            Array.Fill(matchingIsolate, -1, 0, count);

            MatchIsolates(originalTypes, count, matchingPdi, matchingIsolate);

            var baseLevel = baseRtl ? (byte)1 : (byte)0;
            ResolveExplicitLevels(types, originalTypes, levels, removed, matchingPdi, baseLevel, count);

            var visibleCount = BuildVisibleIndices(count, removed, visibleIndices, visibleIndexMap);
            if (visibleCount == 0)
            {
                ApplyL1(count, types, removed, baseLevel, levels);
                ExpandLevelsToChars(count, charStarts, charLengths, levels, charLevels);
                return charLevels;
            }

            var runCount = BuildRuns(visibleIndices, visibleCount, levels, runs, runIndexOfVisible);
            ResolveIsolatingRunSequences(
                visibleIndices,
                visibleCount,
                visibleIndexMap,
                runs,
                runCount,
                runIndexOfVisible,
                types,
                originalTypes,
                levels,
                codepoints,
                matchingPdi,
                matchingIsolate,
                baseLevel);

            ApplyImplicitLevels(count, types, removed, levels);
            ApplyL1(count, types, removed, baseLevel, levels);
            ExpandLevelsToChars(count, charStarts, charLengths, levels, charLevels);
            return charLevels;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(codepoints);
            ArrayPool<BidiClass>.Shared.Return(originalTypes);
            ArrayPool<BidiClass>.Shared.Return(types);
            ArrayPool<byte>.Shared.Return(levels);
            ArrayPool<int>.Shared.Return(charStarts);
            ArrayPool<byte>.Shared.Return(charLengths);
            ArrayPool<bool>.Shared.Return(removed);
            ArrayPool<int>.Shared.Return(matchingPdi);
            ArrayPool<int>.Shared.Return(matchingIsolate);
            ArrayPool<int>.Shared.Return(visibleIndices);
            ArrayPool<int>.Shared.Return(visibleIndexMap);
            ArrayPool<int>.Shared.Return(runIndexOfVisible);
            ArrayPool<LevelRun>.Shared.Return(runs);
        }
    }

    private static int DecodeText(
        ReadOnlySpan<char> text,
        int[] codepoints,
        int[] charStarts,
        byte[] charLengths,
        BidiClass[] originalTypes,
        BidiClass[] types)
    {
        var count = 0;
        var index = 0;
        while (index < text.Length)
        {
            if (!Utf16Decoder.TryDecodeFromUtf16(text[index..], out var rune, out var consumed))
            {
                rune = new Rune(text[index]);
                consumed = 1;
            }

            var codepoint = rune.Value;
            codepoints[count] = codepoint;
            charStarts[count] = index;
            charLengths[count] = (byte)consumed;

            var klass = GetBidiClass(codepoint);
            originalTypes[count] = klass;
            types[count] = klass;

            count++;
            index += consumed;
        }

        return count;
    }

    private static void MatchIsolates(
        BidiClass[] originalTypes,
        int count,
        int[] matchingPdi,
        int[] matchingIsolate)
    {
        var stack = ArrayPool<int>.Shared.Rent(count);
        var depth = 0;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var type = originalTypes[i];
                if (IsIsolateInitiator(type))
                {
                    stack[depth++] = i;
                    continue;
                }

                if (type != BidiClass.PDI || depth <= 0)
                {
                    continue;
                }

                var openIndex = stack[--depth];
                matchingPdi[openIndex] = i;
                matchingIsolate[i] = openIndex;
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(stack);
        }
    }

    private static void ResolveExplicitLevels(
        BidiClass[] types,
        BidiClass[] originalTypes,
        byte[] levels,
        bool[] removed,
        int[] matchingPdi,
        byte baseLevel,
        int count)
    {
        var stackLevels = ArrayPool<byte>.Shared.Rent(MaxDepth + 2);
        var stackOverrides = ArrayPool<BidiClass>.Shared.Rent(MaxDepth + 2);
        var stackIsolate = ArrayPool<bool>.Shared.Rent(MaxDepth + 2);

        var stackTop = 0;
        var overflowIsolateCount = 0;
        var overflowEmbeddingCount = 0;
        var validIsolateCount = 0;

        stackLevels[0] = baseLevel;
        stackOverrides[0] = BidiClass.ON;
        stackIsolate[0] = false;

        try
        {
            for (var i = 0; i < count; i++)
            {
                var type = types[i];
                var newLevel = (byte)0;
                var overrideType = BidiClass.ON;

                switch (type)
                {
                    case BidiClass.RLE:
                    case BidiClass.LRE:
                    case BidiClass.RLO:
                    case BidiClass.LRO:
                        levels[i] = stackLevels[stackTop];
                        removed[i] = true;
                        types[i] = BidiClass.BN;

                        newLevel = type == BidiClass.RLE || type == BidiClass.RLO
                            ? NextOdd(stackLevels[stackTop])
                            : NextEven(stackLevels[stackTop]);

                        if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                        {
                            stackTop++;
                            stackLevels[stackTop] = newLevel;
                            stackOverrides[stackTop] = type == BidiClass.RLO ? BidiClass.R
                                : type == BidiClass.LRO ? BidiClass.L
                                : BidiClass.ON;
                            stackIsolate[stackTop] = false;
                        }
                        else if (overflowIsolateCount == 0)
                        {
                            overflowEmbeddingCount++;
                        }

                        break;
                    case BidiClass.RLI:
                    case BidiClass.LRI:
                    case BidiClass.FSI:
                        levels[i] = stackLevels[stackTop];
                        overrideType = stackOverrides[stackTop];
                        if (overrideType == BidiClass.L)
                        {
                            types[i] = BidiClass.L;
                        }
                        else if (overrideType == BidiClass.R)
                        {
                            types[i] = BidiClass.R;
                        }

                        var treatedAsRtl = type == BidiClass.RLI;
                        if (type == BidiClass.FSI)
                        {
                            treatedAsRtl = ResolveFsiIsRtl(i, matchingPdi, originalTypes, count, baseLevel);
                        }

                        newLevel = treatedAsRtl ? NextOdd(stackLevels[stackTop]) : NextEven(stackLevels[stackTop]);
                        if (newLevel <= MaxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                        {
                            stackTop++;
                            stackLevels[stackTop] = newLevel;
                            stackOverrides[stackTop] = BidiClass.ON;
                            stackIsolate[stackTop] = true;
                            validIsolateCount++;
                        }
                        else
                        {
                            overflowIsolateCount++;
                        }

                        break;
                    case BidiClass.PDI:
                        if (overflowIsolateCount > 0)
                        {
                            overflowIsolateCount--;
                            levels[i] = stackLevels[stackTop];
                            break;
                        }

                        if (validIsolateCount > 0)
                        {
                            while (stackTop > 0 && !stackIsolate[stackTop])
                            {
                                stackTop--;
                            }

                            if (stackTop > 0)
                            {
                                stackTop--;
                            }

                            validIsolateCount--;
                        }

                        levels[i] = stackLevels[stackTop];
                        break;
                    case BidiClass.PDF:
                        levels[i] = stackLevels[stackTop];
                        removed[i] = true;
                        types[i] = BidiClass.BN;

                        if (overflowIsolateCount > 0)
                        {
                            break;
                        }

                        if (overflowEmbeddingCount > 0)
                        {
                            overflowEmbeddingCount--;
                        }
                        else if (stackTop > 0 && !stackIsolate[stackTop])
                        {
                            stackTop--;
                        }

                        break;
                    case BidiClass.B:
                        levels[i] = baseLevel;
                        stackTop = 0;
                        overflowIsolateCount = 0;
                        overflowEmbeddingCount = 0;
                        validIsolateCount = 0;
                        stackLevels[0] = baseLevel;
                        stackOverrides[0] = BidiClass.ON;
                        stackIsolate[0] = false;
                        removed[i] = true;
                        break;
                    case BidiClass.BN:
                        levels[i] = stackLevels[stackTop];
                        removed[i] = true;
                        break;
                    default:
                        levels[i] = stackLevels[stackTop];
                        overrideType = stackOverrides[stackTop];
                        if (overrideType == BidiClass.L)
                        {
                            types[i] = BidiClass.L;
                        }
                        else if (overrideType == BidiClass.R)
                        {
                            types[i] = BidiClass.R;
                        }

                        break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(stackLevels);
            ArrayPool<BidiClass>.Shared.Return(stackOverrides);
            ArrayPool<bool>.Shared.Return(stackIsolate);
        }
    }

    private static bool ResolveFsiIsRtl(
        int index,
        int[] matchingPdi,
        BidiClass[] originalTypes,
        int count,
        byte baseLevel)
    {
        var end = matchingPdi[index];
        if (end < 0)
        {
            end = count;
        }

        for (var i = index + 1; i < end; i++)
        {
            var type = originalTypes[i];
            if (type == BidiClass.L)
            {
                return false;
            }

            if (type == BidiClass.R || type == BidiClass.AL)
            {
                return true;
            }
        }

        return (baseLevel & 1) == 1;
    }

    private static int BuildVisibleIndices(
        int count,
        bool[] removed,
        int[] visibleIndices,
        int[] visibleIndexMap)
    {
        Array.Fill(visibleIndexMap, -1, 0, count);

        var visibleCount = 0;
        for (var i = 0; i < count; i++)
        {
            if (removed[i])
            {
                continue;
            }

            visibleIndexMap[i] = visibleCount;
            visibleIndices[visibleCount++] = i;
        }

        return visibleCount;
    }

    private static int BuildRuns(
        int[] visibleIndices,
        int visibleCount,
        byte[] levels,
        LevelRun[] runs,
        int[] runIndexOfVisible)
    {
        if (visibleCount == 0)
        {
            return 0;
        }

        var runCount = 0;
        var runStart = 0;
        var runLevel = levels[visibleIndices[0]];
        for (var i = 1; i < visibleCount; i++)
        {
            var level = levels[visibleIndices[i]];
            if (level == runLevel)
            {
                continue;
            }

            runs[runCount] = new LevelRun(runStart, i - runStart, runLevel);
            for (var j = runStart; j < i; j++)
            {
                runIndexOfVisible[j] = runCount;
            }

            runCount++;
            runStart = i;
            runLevel = level;
        }

        runs[runCount] = new LevelRun(runStart, visibleCount - runStart, runLevel);
        for (var j = runStart; j < visibleCount; j++)
        {
            runIndexOfVisible[j] = runCount;
        }

        return runCount + 1;
    }

    private static void ResolveIsolatingRunSequences(
        int[] visibleIndices,
        int visibleCount,
        int[] visibleIndexMap,
        LevelRun[] runs,
        int runCount,
        int[] runIndexOfVisible,
        BidiClass[] types,
        BidiClass[] originalTypes,
        byte[] levels,
        int[] codepoints,
        int[] matchingPdi,
        int[] matchingIsolate,
        byte baseLevel)
    {
        if (runCount == 0)
        {
            return;
        }

        var runAssigned = ArrayPool<bool>.Shared.Rent(runCount);
        var runSequence = ArrayPool<int>.Shared.Rent(runCount);
        Array.Fill(runAssigned, false, 0, runCount);

        try
        {
            for (var runIndex = 0; runIndex < runCount; runIndex++)
            {
                if (runAssigned[runIndex])
                {
                    continue;
                }

                var runStart = runs[runIndex].Start;
                var firstIndex = visibleIndices[runStart];
                if (originalTypes[firstIndex] == BidiClass.PDI && matchingIsolate[firstIndex] >= 0)
                {
                    continue;
                }

                var seqRunCount = 0;
                var currentRun = runIndex;
                while (true)
                {
                    runAssigned[currentRun] = true;
                    runSequence[seqRunCount++] = currentRun;

                    var lastPos = runs[currentRun].Start + runs[currentRun].Length - 1;
                    var lastIndex = visibleIndices[lastPos];
                    if (!IsIsolateInitiator(originalTypes[lastIndex]))
                    {
                        break;
                    }

                    var pdiIndex = matchingPdi[lastIndex];
                    if (pdiIndex < 0)
                    {
                        break;
                    }

                    var pdiPos = visibleIndexMap[pdiIndex];
                    if (pdiPos < 0)
                    {
                        break;
                    }

                    var nextRun = runIndexOfVisible[pdiPos];
                    if (pdiPos != runs[nextRun].Start || runAssigned[nextRun])
                    {
                        break;
                    }

                    currentRun = nextRun;
                }

                var seqLen = 0;
                for (var i = 0; i < seqRunCount; i++)
                {
                    seqLen += runs[runSequence[i]].Length;
                }

                if (seqLen == 0)
                {
                    continue;
                }

                var sequence = ArrayPool<int>.Shared.Rent(seqLen);
                try
                {
                    var offset = 0;
                    for (var i = 0; i < seqRunCount; i++)
                    {
                        var run = runs[runSequence[i]];
                        Array.Copy(visibleIndices, run.Start, sequence, offset, run.Length);
                        offset += run.Length;
                    }

                    var firstPos = runs[runSequence[0]].Start;
                    var lastRun = runs[runSequence[seqRunCount - 1]];
                    var lastPos = lastRun.Start + lastRun.Length - 1;
                    var prevLevel = firstPos > 0 ? levels[visibleIndices[firstPos - 1]] : baseLevel;
                    var nextLevel = lastPos + 1 < visibleCount ? levels[visibleIndices[lastPos + 1]] : baseLevel;
                    var startLevel = levels[sequence[0]];
                    var endLevel = levels[sequence[seqLen - 1]];
                    var sosLevel = Math.Max(startLevel, prevLevel);
                    var eosLevel = Math.Max(endLevel, nextLevel);
                    var sos = (sosLevel & 1) == 0 ? BidiClass.L : BidiClass.R;
                    var eos = (eosLevel & 1) == 0 ? BidiClass.L : BidiClass.R;

                    ResolveWeakTypes(sequence, seqLen, sos, types);
                    ResolveBracketPairs(sequence, seqLen, sos, types, originalTypes, levels, codepoints);
                    ResolveNeutralTypes(sequence, seqLen, sos, eos, types, levels);
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(sequence);
                }
            }
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(runAssigned);
            ArrayPool<int>.Shared.Return(runSequence);
        }
    }

    private static void ResolveWeakTypes(int[] sequence, int seqLen, BidiClass sos, BidiClass[] types)
    {
        var prevType = sos;
        for (var i = 0; i < seqLen; i++)
        {
            var index = sequence[i];
            var type = types[index];
            if (type == BidiClass.NSM)
            {
                types[index] = prevType;
                continue;
            }

            prevType = type;
        }

        for (var i = 0; i < seqLen; i++)
        {
            var index = sequence[i];
            if (types[index] != BidiClass.EN)
            {
                continue;
            }

            for (var j = i - 1; j >= 0; j--)
            {
                var strong = GetStrongType(types[sequence[j]]);
                if (strong == BidiClass.ON)
                {
                    continue;
                }

                if (strong == BidiClass.AL)
                {
                    types[index] = BidiClass.AN;
                }

                break;
            }
        }

        for (var i = 0; i < seqLen; i++)
        {
            var index = sequence[i];
            if (types[index] == BidiClass.AL)
            {
                types[index] = BidiClass.R;
            }
        }

        for (var i = 1; i < seqLen - 1; i++)
        {
            var index = sequence[i];
            var type = types[index];
            if (type != BidiClass.ES && type != BidiClass.CS)
            {
                continue;
            }

            var prev = types[sequence[i - 1]];
            var next = types[sequence[i + 1]];

            if (type == BidiClass.ES && prev == BidiClass.EN && next == BidiClass.EN)
            {
                types[index] = BidiClass.EN;
            }
            else if (type == BidiClass.CS)
            {
                if (prev == BidiClass.EN && next == BidiClass.EN)
                {
                    types[index] = BidiClass.EN;
                }
                else if (prev == BidiClass.AN && next == BidiClass.AN)
                {
                    types[index] = BidiClass.AN;
                }
            }
        }

        for (var i = 0; i < seqLen;)
        {
            var index = sequence[i];
            if (types[index] != BidiClass.ET)
            {
                i++;
                continue;
            }

            var start = i;
            while (i < seqLen && types[sequence[i]] == BidiClass.ET)
            {
                i++;
            }

            var end = i - 1;
            var leftEn = start > 0 && types[sequence[start - 1]] == BidiClass.EN;
            var rightEn = i < seqLen && types[sequence[i]] == BidiClass.EN;
            if (leftEn || rightEn)
            {
                for (var j = start; j <= end; j++)
                {
                    types[sequence[j]] = BidiClass.EN;
                }
            }
        }

        for (var i = 0; i < seqLen; i++)
        {
            var index = sequence[i];
            var type = types[index];
            if (type == BidiClass.ES || type == BidiClass.CS)
            {
                types[index] = BidiClass.ON;
            }
        }

        for (var i = 0; i < seqLen; i++)
        {
            var index = sequence[i];
            if (types[index] != BidiClass.EN)
            {
                continue;
            }

            for (var j = i - 1; j >= 0; j--)
            {
                var strong = GetStrongType(types[sequence[j]]);
                if (strong == BidiClass.ON)
                {
                    continue;
                }

                if (strong == BidiClass.L)
                {
                    types[index] = BidiClass.L;
                }

                break;
            }
        }
    }

    private static void ResolveBracketPairs(
        int[] sequence,
        int seqLen,
        BidiClass sos,
        BidiClass[] types,
        BidiClass[] originalTypes,
        byte[] levels,
        int[] codepoints)
    {
        if (seqLen <= 1)
        {
            return;
        }

        var pairOpens = ArrayPool<int>.Shared.Rent(seqLen);
        var pairCloses = ArrayPool<int>.Shared.Rent(seqLen);

        try
        {
            Span<int> bracketPositions = stackalloc int[MaxBracketPairs];
            Span<int> bracketMatches = stackalloc int[MaxBracketPairs];
            var stackSize = 0;
            var pairCount = 0;

            for (var pos = 0; pos < seqLen; pos++)
            {
                var index = sequence[pos];
                if (types[index] != BidiClass.ON)
                {
                    continue;
                }

                var codepoint = codepoints[index];
                if (TryGetOpenBracketPair(codepoint, out var close))
                {
                    if (stackSize < MaxBracketPairs)
                    {
                        bracketPositions[stackSize] = pos;
                        bracketMatches[stackSize] = NormalizeBracket(close);
                        stackSize++;
                    }

                    continue;
                }

                if (!TryGetCloseBracketPair(codepoint, out _))
                {
                    continue;
                }

                var normalizedClose = NormalizeBracket(codepoint);
                var match = -1;
                for (var s = stackSize - 1; s >= 0; s--)
                {
                    if (bracketMatches[s] == normalizedClose)
                    {
                        match = s;
                        break;
                    }
                }

                if (match < 0)
                {
                    continue;
                }

                pairOpens[pairCount] = bracketPositions[match];
                pairCloses[pairCount] = pos;
                pairCount++;
                stackSize = match;
            }

            if (pairCount == 0)
            {
                return;
            }

            Array.Sort(pairOpens, pairCloses, 0, pairCount);

            for (var pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                var openPos = pairOpens[pairIndex];
                var closePos = pairCloses[pairIndex];
                if (openPos < 0 || closePos <= openPos)
                {
                    continue;
                }

                var openIndex = sequence[openPos];
                var closeIndex = sequence[closePos];
                var embeddingDirection = (levels[openIndex] & 1) == 0 ? BidiClass.L : BidiClass.R;

                var hasStrong = false;
                var hasStrongMatching = false;
                for (var pos = openPos + 1; pos < closePos; pos++)
                {
                    var strong = GetStrongTypeForBracket(types[sequence[pos]]);
                    if (strong == BidiClass.ON)
                    {
                        continue;
                    }

                    hasStrong = true;
                    if (strong == embeddingDirection)
                    {
                        hasStrongMatching = true;
                        break;
                    }
                }

                if (hasStrongMatching)
                {
                    ApplyBracketType(sequence, seqLen, openPos, closePos, embeddingDirection, types, originalTypes);
                    continue;
                }

                if (hasStrong)
                {
                    var precedingStrong = FindPrevStrong(sequence, openPos - 1, sos, types, GetStrongTypeForBracket);
                    var resolved = precedingStrong != embeddingDirection ? precedingStrong : embeddingDirection;
                    ApplyBracketType(sequence, seqLen, openPos, closePos, resolved, types, originalTypes);
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(pairOpens);
            ArrayPool<int>.Shared.Return(pairCloses);
        }
    }

    private static void ResolveNeutralTypes(
        int[] sequence,
        int seqLen,
        BidiClass sos,
        BidiClass eos,
        BidiClass[] types,
        byte[] levels)
    {
        var i = 0;
        while (i < seqLen)
        {
            var index = sequence[i];
            if (!IsNeutralOrIsolate(types[index]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < seqLen && IsNeutralOrIsolate(types[sequence[i]]))
            {
                i++;
            }

            var end = i - 1;
            var before = FindPrevStrong(sequence, start - 1, sos, types, GetStrongTypeForNeutral);
            var after = FindNextStrong(sequence, seqLen, i, eos, types, GetStrongTypeForNeutral);

            if (before == after)
            {
                for (var pos = start; pos <= end; pos++)
                {
                    types[sequence[pos]] = before;
                }
            }
            else
            {
                for (var pos = start; pos <= end; pos++)
                {
                    var level = levels[sequence[pos]];
                    types[sequence[pos]] = (level & 1) == 0 ? BidiClass.L : BidiClass.R;
                }
            }
        }
    }

    private static void ApplyImplicitLevels(int count, BidiClass[] types, bool[] removed, byte[] levels)
    {
        for (var i = 0; i < count; i++)
        {
            if (removed[i])
            {
                continue;
            }

            var type = types[i];
            var level = levels[i];
            if ((level & 1) == 0)
            {
                if (type == BidiClass.R)
                {
                    levels[i] = (byte)(level + 1);
                }
                else if (type == BidiClass.AN || type == BidiClass.EN)
                {
                    levels[i] = (byte)(level + 2);
                }
            }
            else
            {
                if (type == BidiClass.L || type == BidiClass.AN || type == BidiClass.EN)
                {
                    levels[i] = (byte)(level + 1);
                }
            }
        }
    }

    private static void ApplyL1(int count, BidiClass[] types, bool[] removed, byte baseLevel, byte[] levels)
    {
        for (var i = 0; i < count; i++)
        {
            if (removed[i] || types[i] == BidiClass.B || types[i] == BidiClass.S)
            {
                levels[i] = baseLevel;
            }
        }

        for (var i = count - 1; i >= 0; i--)
        {
            var type = types[i];
            if (type == BidiClass.WS || type == BidiClass.BN || type == BidiClass.B || type == BidiClass.S || removed[i])
            {
                levels[i] = baseLevel;
                continue;
            }

            break;
        }
    }

    private static void ExpandLevelsToChars(
        int count,
        int[] charStarts,
        byte[] charLengths,
        byte[] levels,
        int[] charLevels)
    {
        for (var i = 0; i < count; i++)
        {
            var start = charStarts[i];
            var length = charLengths[i];
            var level = levels[i];
            for (var j = 0; j < length; j++)
            {
                charLevels[start + j] = level;
            }
        }
    }

    private static void ApplyBracketType(
        int[] sequence,
        int seqLen,
        int openPos,
        int closePos,
        BidiClass resolved,
        BidiClass[] types,
        BidiClass[] originalTypes)
    {
        if (resolved != BidiClass.L && resolved != BidiClass.R)
        {
            return;
        }

        var openIndex = sequence[openPos];
        if (types[openIndex] == BidiClass.ON)
        {
            types[openIndex] = resolved;
            ApplyNsmAfterBracket(sequence, seqLen, openPos + 1, resolved, types, originalTypes);
        }

        var closeIndex = sequence[closePos];
        if (types[closeIndex] == BidiClass.ON)
        {
            types[closeIndex] = resolved;
            ApplyNsmAfterBracket(sequence, seqLen, closePos + 1, resolved, types, originalTypes);
        }
    }

    private static void ApplyNsmAfterBracket(
        int[] sequence,
        int seqLen,
        int startPos,
        BidiClass resolved,
        BidiClass[] types,
        BidiClass[] originalTypes)
    {
        for (var pos = startPos; pos < seqLen; pos++)
        {
            var index = sequence[pos];
            if (originalTypes[index] != BidiClass.NSM)
            {
                break;
            }

            types[index] = resolved;
        }
    }

    private static BidiClass FindPrevStrong(
        int[] sequence,
        int startPos,
        BidiClass defaultType,
        BidiClass[] types,
        Func<BidiClass, BidiClass> classify)
    {
        for (var pos = startPos; pos >= 0; pos--)
        {
            var strong = classify(types[sequence[pos]]);
            if (strong != BidiClass.ON)
            {
                return strong;
            }
        }

        return defaultType;
    }

    private static BidiClass FindNextStrong(
        int[] sequence,
        int seqLen,
        int startPos,
        BidiClass defaultType,
        BidiClass[] types,
        Func<BidiClass, BidiClass> classify)
    {
        for (var pos = startPos; pos < seqLen; pos++)
        {
            var strong = classify(types[sequence[pos]]);
            if (strong != BidiClass.ON)
            {
                return strong;
            }
        }

        return defaultType;
    }

    private static BidiClass GetStrongType(BidiClass type)
    {
        if (type == BidiClass.L)
        {
            return BidiClass.L;
        }

        if (type == BidiClass.R)
        {
            return BidiClass.R;
        }

        if (type == BidiClass.AL)
        {
            return BidiClass.AL;
        }

        return BidiClass.ON;
    }

    private static BidiClass GetStrongTypeForBracket(BidiClass type)
    {
        if (type == BidiClass.L)
        {
            return BidiClass.L;
        }

        if (type == BidiClass.R || type == BidiClass.AN || type == BidiClass.EN || type == BidiClass.AL)
        {
            return BidiClass.R;
        }

        return BidiClass.ON;
    }

    private static BidiClass GetStrongTypeForNeutral(BidiClass type)
    {
        if (type == BidiClass.L)
        {
            return BidiClass.L;
        }

        if (type == BidiClass.R || type == BidiClass.AN || type == BidiClass.EN || type == BidiClass.AL)
        {
            return BidiClass.R;
        }

        return BidiClass.ON;
    }

    private static bool IsNeutralOrIsolate(BidiClass type)
    {
        return type == BidiClass.B
               || type == BidiClass.S
               || type == BidiClass.WS
               || type == BidiClass.ON
               || type == BidiClass.LRI
               || type == BidiClass.RLI
               || type == BidiClass.FSI
               || type == BidiClass.PDI;
    }

    private static bool IsIsolateInitiator(BidiClass type)
    {
        return type == BidiClass.LRI || type == BidiClass.RLI || type == BidiClass.FSI;
    }

    private static byte NextOdd(byte level)
    {
        var next = (byte)(level + 1);
        if ((next & 1) == 0)
        {
            next++;
        }

        return next;
    }

    private static byte NextEven(byte level)
    {
        var next = (byte)(level + 1);
        if ((next & 1) == 1)
        {
            next++;
        }

        return next;
    }

    private static bool TryGetOpenBracketPair(int codepoint, out int close)
    {
        var pairs = TextBidiData.OpenBracketPairs;
        var lo = 0;
        var hi = pairs.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var pair = pairs[mid];
            if (codepoint < pair.Open)
            {
                hi = mid - 1;
            }
            else if (codepoint > pair.Open)
            {
                lo = mid + 1;
            }
            else
            {
                close = pair.Close;
                return true;
            }
        }

        close = 0;
        return false;
    }

    private static bool TryGetCloseBracketPair(int codepoint, out int open)
    {
        var pairs = TextBidiData.CloseBracketPairs;
        var lo = 0;
        var hi = pairs.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var pair = pairs[mid];
            if (codepoint < pair.Open)
            {
                hi = mid - 1;
            }
            else if (codepoint > pair.Open)
            {
                lo = mid + 1;
            }
            else
            {
                open = pair.Close;
                return true;
            }
        }

        open = 0;
        return false;
    }

    private static int NormalizeBracket(int codepoint)
    {
        return codepoint switch
        {
            0x3008 => 0x2329,
            0x3009 => 0x232A,
            _ => codepoint
        };
    }

    private static BidiClass GetBidiClass(int codepoint)
    {
        var ranges = SortedBidiRanges;
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
                return range.Class;
            }
        }

        return TextBidiData.DefaultClass;
    }

    private static TextBidiData.BidiRange[] CreateSortedRanges()
    {
        var ranges = TextBidiData.BidiRanges;
        var sorted = new TextBidiData.BidiRange[ranges.Length];
        Array.Copy(ranges, sorted, ranges.Length);
        Array.Sort(sorted, static (left, right) => left.Start.CompareTo(right.Start));
        return sorted;
    }

    private readonly record struct LevelRun(int Start, int Length, byte Level);
}

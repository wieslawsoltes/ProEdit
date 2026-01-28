using System.Buffers;
using System.Globalization;
using System.Text;
using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

internal static class KnuthPlassLineBreaker
{
    private const int KnuthPlassInfinitePenalty = 10000;
    private const int KnuthPlassHyphenPenalty = 50;
    private const float KnuthPlassTolerance = 2.5f;
    private const float KnuthPlassLinePenalty = 10f;
    private const float KnuthPlassFitnessDemerit = 300f;
    private const float KnuthPlassFlaggedDemerit = 100f;
    private static readonly MathLayoutEngine SharedMathLayoutEngine = new MathLayoutEngine();

    private enum LineBreakNodeKind
    {
        Box,
        Glue,
        Penalty
    }

    private readonly struct LineBreakNode
    {
        public LineBreakNodeKind Kind { get; }
        public float Width { get; }
        public float Stretch { get; }
        public float Shrink { get; }
        public int Penalty { get; }
        public bool IsFlagged { get; }
        public int TextOffset { get; }
        public int TextLength { get; }
        public TextStyle? HyphenStyle { get; }
        public float HyphenBaselineOffset { get; }

        private LineBreakNode(
            LineBreakNodeKind kind,
            float width,
            float stretch,
            float shrink,
            int penalty,
            bool isFlagged,
            int textOffset,
            int textLength,
            TextStyle? hyphenStyle,
            float hyphenBaselineOffset)
        {
            Kind = kind;
            Width = width;
            Stretch = stretch;
            Shrink = shrink;
            Penalty = penalty;
            IsFlagged = isFlagged;
            TextOffset = textOffset;
            TextLength = textLength;
            HyphenStyle = hyphenStyle;
            HyphenBaselineOffset = hyphenBaselineOffset;
        }

        public bool IsBreakable => Kind == LineBreakNodeKind.Glue
                                   || (Kind == LineBreakNodeKind.Penalty && Penalty < KnuthPlassInfinitePenalty);

        public static LineBreakNode Box(float width, int textOffset, int textLength)
        {
            return new LineBreakNode(LineBreakNodeKind.Box, width, 0f, 0f, 0, false, textOffset, textLength, null, 0f);
        }

        public static LineBreakNode Glue(float width, float stretch, float shrink, int textOffset, int textLength)
        {
            return new LineBreakNode(LineBreakNodeKind.Glue, width, stretch, shrink, 0, false, textOffset, textLength, null, 0f);
        }

        public static LineBreakNode PenaltyNode(float width, int penalty, bool flagged, int textOffset, TextStyle style, float baselineOffset)
        {
            return new LineBreakNode(LineBreakNodeKind.Penalty, width, 0f, 0f, penalty, flagged, textOffset, 0, style, baselineOffset);
        }
    }

    private readonly struct LineBreakState
    {
        public int Position { get; }
        public int Line { get; }
        public float Demerits { get; }
        public int FitnessClass { get; }
        public bool IsFlagged { get; }
        public int Previous { get; }

        public LineBreakState(int position, int line, float demerits, int fitnessClass, bool isFlagged, int previous)
        {
            Position = position;
            Line = line;
            Demerits = demerits;
            FitnessClass = fitnessClass;
            IsFlagged = isFlagged;
            Previous = previous;
        }
    }

    private readonly struct TextStyleGridKey : IEquatable<TextStyleGridKey>
    {
        private readonly TextStyleKey _styleKey;
        private readonly float _gridSpacing;

        public TextStyleGridKey(TextStyle style, float gridSpacing)
        {
            _styleKey = new TextStyleKey(style);
            _gridSpacing = gridSpacing;
        }

        public bool Equals(TextStyleGridKey other)
        {
            return _styleKey.Equals(other._styleKey) && _gridSpacing.Equals(other._gridSpacing);
        }

        public override bool Equals(object? obj) => obj is TextStyleGridKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_styleKey, _gridSpacing);
    }

    public static bool TryBreakParagraph(
        string text,
        IReadOnlyList<InlineSpan> spans,
        float firstLineWidth,
        float otherLineWidth,
        ITextMeasurer measurer,
        float charGridSpacing,
        LineBreakOptions options,
        out List<ParagraphLineBreak> breaks)
    {
        breaks = new List<ParagraphLineBreak>();
        if (string.IsNullOrEmpty(text) || spans.Count == 0 || firstLineWidth <= 0f || otherLineWidth <= 0f)
        {
            return false;
        }

        if (!TryBuildLineBreakNodes(text, spans, measurer, charGridSpacing, options, out var nodes))
        {
            return false;
        }

        if (!TryComputeKnuthPlassBreaks(nodes, firstLineWidth, otherLineWidth, out var breakpoints))
        {
            return false;
        }

        breaks = BuildLineBreaks(text, nodes, breakpoints, options);
        return breaks.Count > 0;
    }

    private static bool TryBuildLineBreakNodes(
        string text,
        IReadOnlyList<InlineSpan> spans,
        ITextMeasurer measurer,
        float charGridSpacing,
        LineBreakOptions options,
        out List<LineBreakNode> nodes)
    {
        nodes = new List<LineBreakNode>();
        var wordWidthCache = new Dictionary<TextStyleGridKey, Dictionary<TextSliceKey, float>>();
        var spaceWidthCache = new Dictionary<TextStyleGridKey, float>();
        var hyphenWidthCache = new Dictionary<TextStyleGridKey, float>();
        var mathLayoutEngine = SharedMathLayoutEngine;
        var atParagraphStart = true;

        foreach (var span in spans)
        {
            if (span.Image is not null)
            {
                nodes.Add(LineBreakNode.Box(span.Image.Width, span.Start, span.Length));
                atParagraphStart = false;
                continue;
            }

            if (span.Shape is not null)
            {
                nodes.Add(LineBreakNode.Box(span.Shape.Width, span.Start, span.Length));
                atParagraphStart = false;
                continue;
            }

            if (span.Chart is not null)
            {
                nodes.Add(LineBreakNode.Box(span.Chart.Width, span.Start, span.Length));
                atParagraphStart = false;
                continue;
            }

            if (span.Equation is not null)
            {
                var layout = mathLayoutEngine.Layout(span.Equation.Root, span.Style, measurer);
                nodes.Add(LineBreakNode.Box(layout.Width, span.Start, span.Length));
                atParagraphStart = false;
                continue;
            }

            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            var segmentText = span.Text;
            var segmentStartOffset = span.Start;
            var hyphenator = HyphenationManager.ResolveHyphenator(span.Style.Language);
            var index = 0;
            while (index < segmentText.Length)
            {
                var ch = segmentText[index];
                if (ch == '\t')
                {
                    nodes = new List<LineBreakNode>();
                    return false;
                }

                if (ch == ' ')
                {
                    var spaceStart = index;
                    while (index < segmentText.Length && segmentText[index] == ' ')
                    {
                        index++;
                    }

                    var spaceCount = index - spaceStart;
                    var spaceWidth = MeasureSpace(span.Style, measurer, charGridSpacing, spaceWidthCache) * spaceCount;
                    if (spaceCount > 0)
                    {
                        var offset = segmentStartOffset + spaceStart;
                        if (atParagraphStart)
                        {
                            nodes.Add(LineBreakNode.Box(spaceWidth, offset, spaceCount));
                            atParagraphStart = false;
                        }
                        else
                        {
                            var stretch = spaceWidth * LayoutSpacingDefaults.SpaceStretchRatio;
                            var shrink = spaceWidth * LayoutSpacingDefaults.SpaceShrinkRatio;
                            nodes.Add(LineBreakNode.Glue(spaceWidth, stretch, shrink, offset, spaceCount));
                        }
                    }

                    continue;
                }

                var wordStart = index;
                while (index < segmentText.Length && segmentText[index] != ' ' && segmentText[index] != '\t')
                {
                    index++;
                }

                var wordLength = index - wordStart;
                var wordOffset = segmentStartOffset + wordStart;
                AddWordNodes(segmentText, wordStart, wordLength, wordOffset, span.Style, span.BaselineOffset, measurer, charGridSpacing, hyphenator, wordWidthCache, hyphenWidthCache, options, nodes);
                atParagraphStart = false;
            }
        }

        nodes.Add(LineBreakNode.PenaltyNode(0f, -KnuthPlassInfinitePenalty, false, text.Length, new TextStyle(), 0f));
        return nodes.Count > 0;
    }

    private static void AddWordNodes(
        string source,
        int start,
        int length,
        int wordOffset,
        TextStyle style,
        float baselineOffset,
        ITextMeasurer measurer,
        float charGridSpacing,
        Hyphenator? hyphenator,
        Dictionary<TextStyleGridKey, Dictionary<TextSliceKey, float>> wordWidthCache,
        Dictionary<TextStyleGridKey, float> hyphenWidthCache,
        LineBreakOptions options,
        List<LineBreakNode> nodes)
    {
        if (string.IsNullOrEmpty(source) || length <= 0)
        {
            return;
        }

        var wordSpan = source.AsSpan(start, length);
        if (options.UseEastAsianBreakRules && TextScript.ContainsEastAsian(wordSpan))
        {
            AddCjkNodes(source, start, length, wordOffset, style, baselineOffset, measurer, charGridSpacing, wordWidthCache, options, nodes);
            return;
        }

        if (TryAddBreakableNodes(source, start, length, wordOffset, style, baselineOffset, measurer, charGridSpacing, wordWidthCache, nodes))
        {
            return;
        }

        var hyphenPoints = ShouldHyphenate(wordSpan, hyphenator)
            ? hyphenator!.GetHyphenationPoints(source, start, length)
            : Array.Empty<int>();
        if (hyphenPoints.Length == 0)
        {
            var width = MeasureTextCached(source, start, length, style, measurer, charGridSpacing, wordWidthCache);
            nodes.Add(LineBreakNode.Box(width, wordOffset, length));
            return;
        }

        var segmentStart = 0;
        foreach (var point in hyphenPoints)
        {
            if (point <= segmentStart || point >= length)
            {
                continue;
            }

            var segmentLength = point - segmentStart;
            var segmentWidth = MeasureTextCached(source, start + segmentStart, segmentLength, style, measurer, charGridSpacing, wordWidthCache);
            nodes.Add(LineBreakNode.Box(segmentWidth, wordOffset + segmentStart, segmentLength));

            var hyphenWidth = MeasureHyphenCached(style, measurer, charGridSpacing, hyphenWidthCache);
            nodes.Add(LineBreakNode.PenaltyNode(hyphenWidth, KnuthPlassHyphenPenalty, true, wordOffset + point, style, baselineOffset));
            segmentStart = point;
        }

        if (segmentStart < length)
        {
            var segmentLength = length - segmentStart;
            var segmentWidth = MeasureTextCached(source, start + segmentStart, segmentLength, style, measurer, charGridSpacing, wordWidthCache);
            nodes.Add(LineBreakNode.Box(segmentWidth, wordOffset + segmentStart, segmentLength));
        }
    }

    private static bool ShouldHyphenate(ReadOnlySpan<char> word, Hyphenator? hyphenator)
    {
        if (hyphenator is null)
        {
            return false;
        }

        if (word.Length < hyphenator.LeftMin + hyphenator.RightMin + 1)
        {
            return false;
        }

        var index = 0;
        while (index < word.Length)
        {
            if (!Utf16Decoder.TryDecodeFromUtf16(word[index..], out var rune, out var consumed))
            {
                rune = new System.Text.Rune(word[index]);
                consumed = 1;
            }

            if (!System.Text.Rune.IsLetter(rune) || !TextScript.IsLatinChar(rune))
            {
                return false;
            }

            index += consumed;
        }

        return true;
    }

    private static void AddCjkNodes(
        string source,
        int start,
        int length,
        int wordOffset,
        TextStyle style,
        float baselineOffset,
        ITextMeasurer measurer,
        float charGridSpacing,
        Dictionary<TextStyleGridKey, Dictionary<TextSliceKey, float>> wordWidthCache,
        LineBreakOptions options,
        List<LineBreakNode> nodes)
    {
        var span = source.AsSpan(start, length);
        var index = 0;
        while (index < span.Length)
        {
            var clusterLength = GetNextClusterLength(span, index);
            var width = MeasureTextCached(source, start + index, clusterLength, style, measurer, charGridSpacing, wordWidthCache);
            nodes.Add(LineBreakNode.Box(width, wordOffset + index, clusterLength));
            var nextIndex = index + clusterLength;
            if (nextIndex < span.Length)
            {
                if (ShouldAllowCjkBreak(span, index, clusterLength, nextIndex, options))
                {
                    nodes.Add(LineBreakNode.PenaltyNode(0f, 0, false, wordOffset + nextIndex, style, baselineOffset));
                }
            }

            index = nextIndex;
        }
    }

    private static bool ShouldAllowCjkBreak(
        ReadOnlySpan<char> span,
        int currentIndex,
        int currentLength,
        int nextIndex,
        LineBreakOptions options)
    {
        if (!options.UseAltKinsokuLineBreakRules && !options.DoNotWrapTextWithPunctuation)
        {
            return true;
        }

        var currentRune = GetClusterRune(span.Slice(currentIndex, currentLength));
        var nextLength = GetNextClusterLength(span, nextIndex);
        var nextRune = GetClusterRune(span.Slice(nextIndex, nextLength));

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

    private static bool TryAddBreakableNodes(
        string source,
        int start,
        int length,
        int wordOffset,
        TextStyle style,
        float baselineOffset,
        ITextMeasurer measurer,
        float charGridSpacing,
        Dictionary<TextStyleGridKey, Dictionary<TextSliceKey, float>> wordWidthCache,
        List<LineBreakNode> nodes)
    {
        var span = source.AsSpan(start, length);
        var hasBreak = false;
        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (ch == '-' || ch == '/')
            {
                hasBreak = true;
                break;
            }
        }

        if (!hasBreak)
        {
            return false;
        }

        var segmentStart = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (ch != '-' && ch != '/')
            {
                continue;
            }

            var segmentLength = i + 1 - segmentStart;
            if (segmentLength > 0)
            {
                var width = MeasureTextCached(source, start + segmentStart, segmentLength, style, measurer, charGridSpacing, wordWidthCache);
                nodes.Add(LineBreakNode.Box(width, wordOffset + segmentStart, segmentLength));
            }

            nodes.Add(LineBreakNode.PenaltyNode(0f, 0, false, wordOffset + i + 1, style, baselineOffset));
            segmentStart = i + 1;
        }

        if (segmentStart < length)
        {
            var segmentLength = length - segmentStart;
            var width = MeasureTextCached(source, start + segmentStart, segmentLength, style, measurer, charGridSpacing, wordWidthCache);
            nodes.Add(LineBreakNode.Box(width, wordOffset + segmentStart, segmentLength));
        }

        return true;
    }

    private static int GetNextClusterLength(ReadOnlySpan<char> text, int start)
    {
        return TextCluster.GetNextClusterLength(text, start);
    }

    private static float MeasureTextCached(
        string source,
        int start,
        int length,
        TextStyle style,
        ITextMeasurer measurer,
        float charGridSpacing,
        Dictionary<TextStyleGridKey, Dictionary<TextSliceKey, float>> cache)
    {
        var key = new TextStyleGridKey(style, charGridSpacing);
        if (!cache.TryGetValue(key, out var map))
        {
            map = new Dictionary<TextSliceKey, float>();
            cache[key] = map;
        }

        var slice = new TextSliceKey(source, start, length);
        if (map.TryGetValue(slice, out var width))
        {
            return width;
        }

        var span = source.AsSpan(start, length);
        width = TextGridSnapping.MeasureText(span, style, measurer, charGridSpacing);
        map[slice] = width;
        return width;
    }

    private static float MeasureHyphenCached(
        TextStyle style,
        ITextMeasurer measurer,
        float charGridSpacing,
        Dictionary<TextStyleGridKey, float> cache)
    {
        var key = new TextStyleGridKey(style, charGridSpacing);
        if (cache.TryGetValue(key, out var width))
        {
            return width;
        }

        width = TextGridSnapping.MeasureText("-".AsSpan(), style, measurer, charGridSpacing);
        cache[key] = width;
        return width;
    }

    private static float MeasureSpace(
        TextStyle style,
        ITextMeasurer measurer,
        float charGridSpacing,
        Dictionary<TextStyleGridKey, float> cache)
    {
        var key = new TextStyleGridKey(style, charGridSpacing);
        if (cache.TryGetValue(key, out var width))
        {
            return width;
        }

        width = TextGridSnapping.MeasureText(" ".AsSpan(), style, measurer, charGridSpacing);
        cache[key] = width;
        return width;
    }

    private static bool TryComputeKnuthPlassBreaks(
        IReadOnlyList<LineBreakNode> nodes,
        float firstLineWidth,
        float otherLineWidth,
        out List<int> breakpoints)
    {
        breakpoints = new List<int>();
        if (nodes.Count == 0)
        {
            return false;
        }

        var prefixLength = nodes.Count + 1;
        var pool = ArrayPool<float>.Shared;
        var prefixWidth = pool.Rent(prefixLength);
        var prefixStretch = pool.Rent(prefixLength);
        var prefixShrink = pool.Rent(prefixLength);

        try
        {
            prefixWidth[0] = 0f;
            prefixStretch[0] = 0f;
            prefixShrink[0] = 0f;
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                prefixWidth[i + 1] = prefixWidth[i] + (node.Kind == LineBreakNodeKind.Penalty ? 0f : node.Width);
                prefixStretch[i + 1] = prefixStretch[i] + (node.Kind == LineBreakNodeKind.Glue ? node.Stretch : 0f);
                prefixShrink[i + 1] = prefixShrink[i] + (node.Kind == LineBreakNodeKind.Glue ? node.Shrink : 0f);
            }

            var states = new List<LineBreakState>(nodes.Count) { new LineBreakState(-1, 0, 0f, 1, false, -1) };
            var active = new List<int>(Math.Min(nodes.Count, 64)) { 0 };
            var filteredActive = new List<int>(active.Capacity);
            var newActive = new List<int>(8);
            var boolPool = ArrayPool<bool>.Shared;
            Span<float> bestDemerits = stackalloc float[4];
            Span<int> bestActive = stackalloc int[4];
            Span<bool> bestFlagged = stackalloc bool[4];
            Span<bool> removalStack = stackalloc bool[256];

            for (var breakIndex = 0; breakIndex < nodes.Count; breakIndex++)
            {
                var breakNode = nodes[breakIndex];
                if (!breakNode.IsBreakable)
                {
                    continue;
                }

                bestDemerits[0] = float.PositiveInfinity;
                bestDemerits[1] = float.PositiveInfinity;
                bestDemerits[2] = float.PositiveInfinity;
                bestDemerits[3] = float.PositiveInfinity;
                bestActive.Fill(-1);
                bestFlagged.Clear();

                bool[]? toRemoveBuffer = null;
                var toRemove = active.Count <= removalStack.Length
                    ? removalStack[..active.Count]
                    : (toRemoveBuffer = boolPool.Rent(active.Count));
                toRemove.Clear();
                var hasRemove = false;

                try
                {
                    for (var activeIndex = 0; activeIndex < active.Count; activeIndex++)
                    {
                        var stateIndex = active[activeIndex];
                        var state = states[stateIndex];
                        var lineWidth = state.Line == 0 ? firstLineWidth : otherLineWidth;
                        ComputeLineDimensions(nodes, prefixWidth, prefixStretch, prefixShrink, state.Position, breakIndex,
                            out var width, out var stretch, out var shrink);

                        var difference = lineWidth - width;
                        float ratio;
                        if (difference > 0f)
                        {
                            ratio = stretch > 0f ? difference / stretch : float.PositiveInfinity;
                        }
                        else if (difference < 0f)
                        {
                            ratio = shrink > 0f ? difference / shrink : float.NegativeInfinity;
                        }
                        else
                        {
                            ratio = 0f;
                        }

                        if (ratio < -1f)
                        {
                            toRemove[activeIndex] = true;
                            hasRemove = true;
                            continue;
                        }

                        if (ratio > KnuthPlassTolerance)
                        {
                            continue;
                        }

                        var badness = 100f * MathF.Pow(MathF.Abs(ratio), 3f);
                        var fitness = GetFitnessClass(ratio);
                        var demerits = KnuthPlassLinePenalty + badness;
                        demerits *= demerits;

                        if (breakNode.Penalty >= 0)
                        {
                            demerits += breakNode.Penalty * breakNode.Penalty;
                        }
                        else if (breakNode.Penalty > -KnuthPlassInfinitePenalty)
                        {
                            demerits -= breakNode.Penalty * breakNode.Penalty;
                        }

                        if (breakNode.IsFlagged && state.IsFlagged)
                        {
                            demerits += KnuthPlassFlaggedDemerit;
                        }

                        if (Math.Abs(fitness - state.FitnessClass) > 1)
                        {
                            demerits += KnuthPlassFitnessDemerit;
                        }

                        var total = state.Demerits + demerits;
                        if (total < bestDemerits[fitness])
                        {
                            bestDemerits[fitness] = total;
                            bestActive[fitness] = stateIndex;
                            bestFlagged[fitness] = breakNode.IsFlagged;
                        }
                    }

                    if (hasRemove)
                    {
                        filteredActive.Clear();
                        for (var i = 0; i < active.Count; i++)
                        {
                            if (!toRemove[i])
                            {
                                filteredActive.Add(active[i]);
                            }
                        }

                        var swap = active;
                        active = filteredActive;
                        filteredActive = swap;
                    }

                    newActive.Clear();
                    for (var fitness = 0; fitness < bestActive.Length; fitness++)
                    {
                        var prevIndex = bestActive[fitness];
                        if (prevIndex < 0)
                        {
                            continue;
                        }

                        var prevState = states[prevIndex];
                        var newState = new LineBreakState(breakIndex, prevState.Line + 1, bestDemerits[fitness], fitness, bestFlagged[fitness], prevIndex);
                        states.Add(newState);
                        newActive.Add(states.Count - 1);
                    }

                    if (breakNode.Penalty <= -KnuthPlassInfinitePenalty)
                    {
                        active = newActive;
                        break;
                    }

                    if (newActive.Count > 0)
                    {
                        active.AddRange(newActive);
                    }
                }
                finally
                {
                    if (toRemoveBuffer is not null)
                    {
                        boolPool.Return(toRemoveBuffer);
                    }
                }
            }

            if (active.Count == 0)
            {
                return false;
            }

            var bestFinal = active[0];
            for (var i = 1; i < active.Count; i++)
            {
                if (states[active[i]].Demerits < states[bestFinal].Demerits)
                {
                    bestFinal = active[i];
                }
            }

            var current = bestFinal;
            while (current >= 0)
            {
                var state = states[current];
                if (state.Position >= 0)
                {
                    breakpoints.Add(state.Position);
                }

                current = state.Previous;
            }

            breakpoints.Reverse();
            return breakpoints.Count > 0;
        }
        finally
        {
            pool.Return(prefixWidth);
            pool.Return(prefixStretch);
            pool.Return(prefixShrink);
        }
    }

    private static void ComputeLineDimensions(
        IReadOnlyList<LineBreakNode> nodes,
        float[] prefixWidth,
        float[] prefixStretch,
        float[] prefixShrink,
        int fromBreak,
        int toBreak,
        out float width,
        out float stretch,
        out float shrink)
    {
        var start = Math.Max(0, fromBreak + 1);
        var end = Math.Min(nodes.Count - 1, toBreak);
        width = prefixWidth[end + 1] - prefixWidth[start];
        stretch = prefixStretch[end + 1] - prefixStretch[start];
        shrink = prefixShrink[end + 1] - prefixShrink[start];

        var breakNode = nodes[end];
        if (breakNode.Kind == LineBreakNodeKind.Glue)
        {
            width -= breakNode.Width;
            stretch -= breakNode.Stretch;
            shrink -= breakNode.Shrink;
        }
        else if (breakNode.Kind == LineBreakNodeKind.Penalty)
        {
            width += breakNode.Width;
        }
    }

    private static int GetFitnessClass(float ratio)
    {
        if (ratio < -0.5f)
        {
            return 0;
        }

        if (ratio <= 0.5f)
        {
            return 1;
        }

        return ratio <= 1f ? 2 : 3;
    }

    private static List<ParagraphLineBreak> BuildLineBreaks(
        string text,
        IReadOnlyList<LineBreakNode> nodes,
        IReadOnlyList<int> breakpoints,
        LineBreakOptions options)
    {
        var lines = new List<ParagraphLineBreak>();
        var previous = -1;
        foreach (var breakIndex in breakpoints)
        {
            var lineStartOffset = ResolveLineStartOffset(nodes, previous, options);
            var breakNode = nodes[Math.Clamp(breakIndex, 0, nodes.Count - 1)];
            var lineEndOffset = breakNode.TextOffset;
            if (lineEndOffset < lineStartOffset)
            {
                lineEndOffset = lineStartOffset;
            }

            var length = lineEndOffset - lineStartOffset;
            var hasHyphen = breakNode.Kind == LineBreakNodeKind.Penalty
                && breakNode.Penalty < KnuthPlassInfinitePenalty
                && breakNode.Width > 0f;
            var hyphenStyle = hasHyphen ? breakNode.HyphenStyle : null;
            var hyphenBaselineOffset = hasHyphen ? breakNode.HyphenBaselineOffset : 0f;
            lines.Add(new ParagraphLineBreak(lineStartOffset, length, hasHyphen, hyphenStyle, hyphenBaselineOffset));
            previous = breakIndex;
        }

        return lines;
    }

    private static int ResolveLineStartOffset(IReadOnlyList<LineBreakNode> nodes, int previousBreak, LineBreakOptions options)
    {
        var startOffset = -1;
        var index = Math.Max(0, previousBreak + 1);
        if (options.WrapTrailingSpaces && previousBreak >= 0 && previousBreak < nodes.Count)
        {
            var previousNode = nodes[previousBreak];
            if (previousNode.Kind == LineBreakNodeKind.Glue)
            {
                return previousNode.TextOffset;
            }
        }

        if (!options.WrapTrailingSpaces)
        {
            while (index < nodes.Count && nodes[index].Kind == LineBreakNodeKind.Glue)
            {
                startOffset = nodes[index].TextOffset + nodes[index].TextLength;
                index++;
            }
        }

        if (startOffset >= 0)
        {
            return startOffset;
        }

        if (index < nodes.Count)
        {
            return nodes[index].TextOffset;
        }

        return nodes.Count > 0 ? nodes[^1].TextOffset : 0;
    }

}

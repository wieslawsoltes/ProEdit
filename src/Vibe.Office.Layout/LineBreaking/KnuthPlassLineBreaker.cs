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
    private static readonly Lazy<Hyphenator> DefaultHyphenator = new Lazy<Hyphenator>(Hyphenator.CreateDefault);

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

    private sealed class LineBreakState
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

    public static bool TryBreakParagraph(
        string text,
        IReadOnlyList<InlineSpan> spans,
        float firstLineWidth,
        float otherLineWidth,
        ITextMeasurer measurer,
        out List<ParagraphLineBreak> breaks)
    {
        breaks = new List<ParagraphLineBreak>();
        if (string.IsNullOrEmpty(text) || spans.Count == 0 || firstLineWidth <= 0f || otherLineWidth <= 0f)
        {
            return false;
        }

        if (!TryBuildLineBreakNodes(text, spans, measurer, out var nodes))
        {
            return false;
        }

        if (!TryComputeKnuthPlassBreaks(nodes, firstLineWidth, otherLineWidth, out var breakpoints))
        {
            return false;
        }

        breaks = BuildLineBreaks(text, nodes, breakpoints);
        return breaks.Count > 0;
    }

    private static bool TryBuildLineBreakNodes(
        string text,
        IReadOnlyList<InlineSpan> spans,
        ITextMeasurer measurer,
        out List<LineBreakNode> nodes)
    {
        nodes = new List<LineBreakNode>();
        var hyphenator = DefaultHyphenator.Value;
        var wordWidthCache = new Dictionary<TextStyleKey, Dictionary<string, float>>();
        var spaceWidthCache = new Dictionary<TextStyleKey, float>();
        var hyphenWidthCache = new Dictionary<TextStyleKey, float>();
        var mathLayoutEngine = new MathLayoutEngine();
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
                    var spaceWidth = MeasureSpace(span.Style, measurer, spaceWidthCache) * spaceCount;
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

                var word = segmentText.Substring(wordStart, index - wordStart);
                var wordOffset = segmentStartOffset + wordStart;
                AddWordNodes(word, wordOffset, span.Style, span.BaselineOffset, measurer, hyphenator, wordWidthCache, hyphenWidthCache, nodes);
                atParagraphStart = false;
            }
        }

        nodes.Add(LineBreakNode.PenaltyNode(0f, -KnuthPlassInfinitePenalty, false, text.Length, new TextStyle(), 0f));
        return nodes.Count > 0;
    }

    private static void AddWordNodes(
        string word,
        int wordOffset,
        TextStyle style,
        float baselineOffset,
        ITextMeasurer measurer,
        Hyphenator hyphenator,
        Dictionary<TextStyleKey, Dictionary<string, float>> wordWidthCache,
        Dictionary<TextStyleKey, float> hyphenWidthCache,
        List<LineBreakNode> nodes)
    {
        if (string.IsNullOrEmpty(word))
        {
            return;
        }

        var hyphenPoints = ShouldHyphenate(word, hyphenator) ? hyphenator.GetHyphenationPoints(word) : Array.Empty<int>();
        if (hyphenPoints.Length == 0)
        {
            var width = MeasureTextCached(word, style, measurer, wordWidthCache);
            nodes.Add(LineBreakNode.Box(width, wordOffset, word.Length));
            return;
        }

        var segmentStart = 0;
        foreach (var point in hyphenPoints)
        {
            if (point <= segmentStart || point >= word.Length)
            {
                continue;
            }

            var segment = word.Substring(segmentStart, point - segmentStart);
            var segmentWidth = MeasureTextCached(segment, style, measurer, wordWidthCache);
            nodes.Add(LineBreakNode.Box(segmentWidth, wordOffset + segmentStart, segment.Length));

            var hyphenWidth = MeasureHyphenCached(style, measurer, hyphenWidthCache);
            nodes.Add(LineBreakNode.PenaltyNode(hyphenWidth, KnuthPlassHyphenPenalty, true, wordOffset + point, style, baselineOffset));
            segmentStart = point;
        }

        if (segmentStart < word.Length)
        {
            var segment = word.Substring(segmentStart);
            var segmentWidth = MeasureTextCached(segment, style, measurer, wordWidthCache);
            nodes.Add(LineBreakNode.Box(segmentWidth, wordOffset + segmentStart, segment.Length));
        }
    }

    private static bool ShouldHyphenate(string word, Hyphenator hyphenator)
    {
        if (word.Length < hyphenator.LeftMin + hyphenator.RightMin + 1)
        {
            return false;
        }

        foreach (var ch in word)
        {
            if (!char.IsLetter(ch) || !TextScript.IsLatinChar(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static float MeasureTextCached(
        string text,
        TextStyle style,
        ITextMeasurer measurer,
        Dictionary<TextStyleKey, Dictionary<string, float>> cache)
    {
        var key = new TextStyleKey(style);
        if (!cache.TryGetValue(key, out var map))
        {
            map = new Dictionary<string, float>(StringComparer.Ordinal);
            cache[key] = map;
        }

        if (map.TryGetValue(text, out var width))
        {
            return width;
        }

        width = measurer.MeasureText(text, style).Width;
        map[text] = width;
        return width;
    }

    private static float MeasureHyphenCached(
        TextStyle style,
        ITextMeasurer measurer,
        Dictionary<TextStyleKey, float> cache)
    {
        var key = new TextStyleKey(style);
        if (cache.TryGetValue(key, out var width))
        {
            return width;
        }

        width = measurer.MeasureText("-", style).Width;
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

        var prefixWidth = new float[nodes.Count + 1];
        var prefixStretch = new float[nodes.Count + 1];
        var prefixShrink = new float[nodes.Count + 1];
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            prefixWidth[i + 1] = prefixWidth[i] + (node.Kind == LineBreakNodeKind.Penalty ? 0f : node.Width);
            prefixStretch[i + 1] = prefixStretch[i] + (node.Kind == LineBreakNodeKind.Glue ? node.Stretch : 0f);
            prefixShrink[i + 1] = prefixShrink[i] + (node.Kind == LineBreakNodeKind.Glue ? node.Shrink : 0f);
        }

        var states = new List<LineBreakState> { new LineBreakState(-1, 0, 0f, 1, false, -1) };
        var active = new List<int> { 0 };

        for (var breakIndex = 0; breakIndex < nodes.Count; breakIndex++)
        {
            var breakNode = nodes[breakIndex];
            if (!breakNode.IsBreakable)
            {
                continue;
            }

            var bestDemerits = new float[4] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            var bestActive = new int[4] { -1, -1, -1, -1 };
            var bestFitness = new int[4];
            var bestFlagged = new bool[4];
            var toRemove = new bool[active.Count];

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
                    bestFitness[fitness] = fitness;
                    bestFlagged[fitness] = breakNode.IsFlagged;
                }
            }

            var hasRemove = false;
            for (var i = 0; i < toRemove.Length; i++)
            {
                if (toRemove[i])
                {
                    hasRemove = true;
                    break;
                }
            }

            if (hasRemove)
            {
                var nextActive = new List<int>(active.Count);
                for (var i = 0; i < active.Count; i++)
                {
                    if (!toRemove[i])
                    {
                        nextActive.Add(active[i]);
                    }
                }

                active = nextActive;
            }

            var newActive = new List<int>();
            for (var fitness = 0; fitness < bestActive.Length; fitness++)
            {
                var prevIndex = bestActive[fitness];
                if (prevIndex < 0)
                {
                    continue;
                }

                var prevState = states[prevIndex];
                var newState = new LineBreakState(breakIndex, prevState.Line + 1, bestDemerits[fitness], bestFitness[fitness], bestFlagged[fitness], prevIndex);
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
        IReadOnlyList<int> breakpoints)
    {
        var lines = new List<ParagraphLineBreak>();
        var previous = -1;
        foreach (var breakIndex in breakpoints)
        {
            var lineStartOffset = ResolveLineStartOffset(nodes, previous);
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

    private static int ResolveLineStartOffset(IReadOnlyList<LineBreakNode> nodes, int previousBreak)
    {
        var startOffset = -1;
        var index = Math.Max(0, previousBreak + 1);
        while (index < nodes.Count && nodes[index].Kind == LineBreakNodeKind.Glue)
        {
            startOffset = nodes[index].TextOffset + nodes[index].TextLength;
            index++;
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

    private static float MeasureSpace(TextStyle style, ITextMeasurer measurer, Dictionary<TextStyleKey, float> cache)
    {
        var key = new TextStyleKey(style);
        if (cache.TryGetValue(key, out var width))
        {
            return width;
        }

        width = measurer.MeasureText(" ", style).Width;
        cache[key] = width;
        return width;
    }
}

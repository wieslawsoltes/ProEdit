using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

internal static class LineJustifier
{
    private const float MaxLetterSpacingEm = 0.05f;
    private const float MaxLetterSpacingShrinkEm = 0.02f;

    public static LineLayout Justify(LineLayout layout, float targetWidth, ITextMeasurer measurer)
    {
        if (layout.Width <= 0f || targetWidth <= layout.Width + 0.01f)
        {
            return layout;
        }

        var extra = targetWidth - layout.Width;
        if (MathF.Abs(extra) <= 0.01f)
        {
            return layout;
        }

        var spaceWidthCache = new Dictionary<TextStyleKey, float>();
        var (spaceCount, totalSpaceWidth) = MeasureSpaces(layout, measurer, spaceWidthCache);
        if (spaceCount == 0 && IsCjkLine(layout))
        {
            return BuildCjkJustifiedLayout(layout, measurer, extra);
        }

        var remaining = extra;
        var spaceContribution = 0f;
        if (spaceCount > 0 && totalSpaceWidth > 0f)
        {
            var stretchCap = totalSpaceWidth * LayoutSpacingDefaults.SpaceStretchRatio;
            var shrinkCap = totalSpaceWidth * LayoutSpacingDefaults.SpaceShrinkRatio;
            spaceContribution = Math.Clamp(remaining, -shrinkCap, stretchCap);
            remaining -= spaceContribution;
        }

        var letterGapUnits = 0f;
        Dictionary<TextStyleKey, Dictionary<string, int>>? letterGapCache = null;
        var letterContribution = 0f;
        if (remaining != 0f && measurer is ITextMeasurerAdvanced advanced)
        {
            letterGapCache = new Dictionary<TextStyleKey, Dictionary<string, int>>();
            letterGapUnits = MeasureLatinLetterGapUnits(layout, advanced, letterGapCache);
            if (letterGapUnits > 0f)
            {
                var stretchCap = letterGapUnits * MaxLetterSpacingEm;
                var shrinkCap = letterGapUnits * MaxLetterSpacingShrinkEm;
                letterContribution = Math.Clamp(remaining, -shrinkCap, stretchCap);
                remaining -= letterContribution;
            }
        }

        if (spaceCount > 0 && totalSpaceWidth > 0f)
        {
            spaceContribution += remaining;
        }
        else
        {
            letterContribution += remaining;
        }

        var result = layout;
        if (spaceCount > 0 && totalSpaceWidth > 0f && MathF.Abs(spaceContribution) > 0.001f)
        {
            var spaceScale = spaceContribution / totalSpaceWidth;
            result = BuildSpaceJustifiedLayout(result, measurer, spaceWidthCache, spaceScale);
        }

        if (letterContribution != 0f && letterGapUnits > 0f && measurer is ITextMeasurerAdvanced advancedSpacing)
        {
            var letterSpacingEm = letterContribution / letterGapUnits;
            result = ApplyLetterSpacing(result, letterSpacingEm, advancedSpacing, letterGapCache ?? new Dictionary<TextStyleKey, Dictionary<string, int>>());
        }

        return result;
    }

    private static (int Count, float TotalWidth) MeasureSpaces(
        LineLayout layout,
        ITextMeasurer measurer,
        Dictionary<TextStyleKey, float> cache)
    {
        var count = 0;
        var total = 0f;
        foreach (var run in layout.Runs)
        {
            if (run.IsTab || string.IsNullOrEmpty(run.Text))
            {
                if (run.IsTab)
                {
                    return (0, 0f);
                }

                continue;
            }

            var runSpaceCount = 0;
            foreach (var ch in run.Text)
            {
                if (ch == ' ')
                {
                    runSpaceCount++;
                }
            }

            if (runSpaceCount > 0)
            {
                var spaceWidth = MeasureSpace(run.Style, measurer, cache);
                count += runSpaceCount;
                total += spaceWidth * runSpaceCount;
            }
        }

        return (count, total);
    }

    private static float MeasureLatinLetterGapUnits(
        LineLayout layout,
        ITextMeasurerAdvanced advanced,
        Dictionary<TextStyleKey, Dictionary<string, int>> cache)
    {
        var units = 0f;
        foreach (var run in layout.Runs)
        {
            if (run.IsTab || string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            var text = run.Text;
            var segmentStart = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                {
                    continue;
                }

                if (i > segmentStart)
                {
                    var token = text.Substring(segmentStart, i - segmentStart);
                    var gapCount = GetLatinGapCountCached(token, run.Style, advanced, cache);
                    if (gapCount > 0)
                    {
                        units += gapCount * run.Style.FontSize;
                    }
                }

                segmentStart = i + 1;
            }

            if (segmentStart < text.Length)
            {
                var token = text.Substring(segmentStart);
                var gapCount = GetLatinGapCountCached(token, run.Style, advanced, cache);
                if (gapCount > 0)
                {
                    units += gapCount * run.Style.FontSize;
                }
            }
        }

        return units;
    }

    private static LineLayout ApplyLetterSpacing(
        LineLayout layout,
        float letterSpacingEm,
        ITextMeasurerAdvanced advanced,
        Dictionary<TextStyleKey, Dictionary<string, int>> cache)
    {
        if (MathF.Abs(letterSpacingEm) < 0.0001f)
        {
            return layout;
        }

        var segments = BuildLayoutSegments(layout);
        var runs = new List<LayoutRun>(layout.Runs.Count);
        var images = new List<LayoutImage>(layout.Images.Count);
        var shapes = new List<LayoutShape>(layout.Shapes.Count);
        var charts = new List<LayoutChart>(layout.Charts.Count);
        var equations = new List<LayoutEquation>(layout.Equations.Count);
        var x = 0f;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            switch (segment.Kind)
            {
                case LayoutSegmentKind.Run:
                {
                    var run = segment.Run!;
                    if (run.IsTab || string.IsNullOrEmpty(run.Text))
                    {
                        runs.Add(run with { X = x });
                        x += run.Width;
                        break;
                    }

                    var gapCount = GetLatinGapCountCached(run.Text, run.Style, advanced, cache);
                    if (gapCount > 0)
                    {
                        var letterSpacing = letterSpacingEm * run.Style.FontSize;
                        var adjustedWidth = run.Width + letterSpacing * gapCount;
                        runs.Add(run with { X = x, Width = adjustedWidth, LetterSpacing = letterSpacing });
                        x += adjustedWidth;
                    }
                    else
                    {
                        runs.Add(run with { X = x, LetterSpacing = 0f });
                        x += run.Width;
                    }

                    break;
                }
                case LayoutSegmentKind.Image:
                {
                    var image = segment.Image!;
                    images.Add(image with { X = x });
                    x += image.Width;
                    break;
                }
                case LayoutSegmentKind.Shape:
                {
                    var shape = segment.Shape!;
                    shapes.Add(shape with { X = x });
                    x += shape.Width;
                    break;
                }
                case LayoutSegmentKind.Chart:
                {
                    var chart = segment.Chart!;
                    charts.Add(chart with { X = x });
                    x += chart.Width;
                    break;
                }
                case LayoutSegmentKind.Equation:
                {
                    var equation = segment.Equation!;
                    equations.Add(equation with { X = x });
                    x += equation.Width;
                    break;
                }
            }
        }

        return layout with
        {
            Runs = runs,
            Images = images,
            Shapes = shapes,
            Charts = charts,
            Equations = equations,
            Width = x
        };
    }

    private static int GetLatinGapCountCached(
        string text,
        TextStyle style,
        ITextMeasurerAdvanced advanced,
        Dictionary<TextStyleKey, Dictionary<string, int>> cache)
    {
        if (string.IsNullOrEmpty(text) || !IsLatinText(text))
        {
            return 0;
        }

        var key = new TextStyleKey(style);
        if (!cache.TryGetValue(key, out var map))
        {
            map = new Dictionary<string, int>(StringComparer.Ordinal);
            cache[key] = map;
        }

        if (map.TryGetValue(text, out var count))
        {
            return count;
        }

        var shaped = advanced.ShapeText(text, style);
        var clusterCount = shaped.ClusterOffsets.Length;
        count = Math.Max(0, clusterCount - 1);
        map[text] = count;
        return count;
    }

    private static bool IsLatinText(string text)
    {
        var hasLetter = false;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch))
            {
                continue;
            }

            hasLetter = true;
            if (!TextScript.IsLatinChar(ch))
            {
                return false;
            }
        }

        return hasLetter;
    }

    private static LineLayout BuildSpaceJustifiedLayout(
        LineLayout layout,
        ITextMeasurer measurer,
        Dictionary<TextStyleKey, float> spaceWidthCache,
        float spaceScale)
    {
        if (MathF.Abs(spaceScale) < 0.001f)
        {
            return layout;
        }

        var segments = BuildLayoutSegments(layout);
        var runs = new List<LayoutRun>(layout.Runs.Count);
        var images = new List<LayoutImage>(layout.Images.Count);
        var shapes = new List<LayoutShape>(layout.Shapes.Count);
        var charts = new List<LayoutChart>(layout.Charts.Count);
        var equations = new List<LayoutEquation>(layout.Equations.Count);
        var x = 0f;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            switch (segment.Kind)
            {
                case LayoutSegmentKind.Run:
                {
                    var run = segment.Run!;
                    if (run.IsTab || string.IsNullOrEmpty(run.Text))
                    {
                        runs.Add(run with { X = x });
                        x += run.Width;
                        break;
                    }

                    var text = run.Text;
                    var segmentStart = 0;
                    for (var chIndex = 0; chIndex < text.Length; chIndex++)
                    {
                        if (text[chIndex] != ' ')
                        {
                            continue;
                        }

                        if (chIndex > segmentStart)
                        {
                            var token = text.Substring(segmentStart, chIndex - segmentStart);
                            var width = measurer.MeasureText(token, run.Style).Width;
                            runs.Add(new LayoutRun(token, run.Style, x, width, token.Length, false, run.BaselineOffset, run.TabLeader));
                            x += width;
                        }

                        var spaceWidth = MeasureSpace(run.Style, measurer, spaceWidthCache);
                        var adjustedWidth = spaceWidth * (1f + spaceScale);
                        runs.Add(new LayoutRun(" ", run.Style, x, adjustedWidth, 1, false, run.BaselineOffset, run.TabLeader));
                        x += adjustedWidth;
                        segmentStart = chIndex + 1;
                    }

                    if (segmentStart < text.Length)
                    {
                        var token = text.Substring(segmentStart);
                        var width = measurer.MeasureText(token, run.Style).Width;
                        runs.Add(new LayoutRun(token, run.Style, x, width, token.Length, false, run.BaselineOffset, run.TabLeader));
                        x += width;
                    }
                    break;
                }
                case LayoutSegmentKind.Image:
                {
                    var image = segment.Image!;
                    images.Add(image with { X = x });
                    x += image.Width;
                    break;
                }
                case LayoutSegmentKind.Shape:
                {
                    var shape = segment.Shape!;
                    shapes.Add(shape with { X = x });
                    x += shape.Width;
                    break;
                }
                case LayoutSegmentKind.Chart:
                {
                    var chart = segment.Chart!;
                    charts.Add(chart with { X = x });
                    x += chart.Width;
                    break;
                }
                case LayoutSegmentKind.Equation:
                {
                    var equation = segment.Equation!;
                    equations.Add(equation with { X = x });
                    x += equation.Width;
                    break;
                }
            }
        }

        return layout with
        {
            Runs = runs,
            Images = images,
            Shapes = shapes,
            Charts = charts,
            Equations = equations,
            Width = x
        };
    }

    private static LineLayout BuildCjkJustifiedLayout(LineLayout layout, ITextMeasurer measurer, float extra)
    {
        var gapCount = 0;
        foreach (var run in layout.Runs)
        {
            if (run.IsTab || string.IsNullOrEmpty(run.Text))
            {
                if (run.IsTab)
                {
                    return layout;
                }

                continue;
            }

            gapCount += Math.Max(0, run.Text.Length - 1);
        }

        if (gapCount <= 0)
        {
            return layout;
        }

        var extraPerGap = extra / gapCount;
        var segments = BuildLayoutSegments(layout);
        var runs = new List<LayoutRun>();
        var images = new List<LayoutImage>(layout.Images.Count);
        var shapes = new List<LayoutShape>(layout.Shapes.Count);
        var charts = new List<LayoutChart>(layout.Charts.Count);
        var equations = new List<LayoutEquation>(layout.Equations.Count);
        var x = 0f;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            switch (segment.Kind)
            {
                case LayoutSegmentKind.Run:
                {
                    var run = segment.Run!;
                    if (run.IsTab || string.IsNullOrEmpty(run.Text))
                    {
                        runs.Add(run with { X = x });
                        x += run.Width;
                        break;
                    }

                    var text = run.Text;
                    for (var chIndex = 0; chIndex < text.Length; chIndex++)
                    {
                        var ch = text[chIndex];
                        var width = measurer.MeasureText(ch.ToString(), run.Style).Width;
                        if (chIndex < text.Length - 1)
                        {
                            width += extraPerGap;
                        }

                        runs.Add(new LayoutRun(ch.ToString(), run.Style, x, width, 1, false, run.BaselineOffset, run.TabLeader));
                        x += width;
                    }
                    break;
                }
                case LayoutSegmentKind.Image:
                {
                    var image = segment.Image!;
                    images.Add(image with { X = x });
                    x += image.Width;
                    break;
                }
                case LayoutSegmentKind.Shape:
                {
                    var shape = segment.Shape!;
                    shapes.Add(shape with { X = x });
                    x += shape.Width;
                    break;
                }
                case LayoutSegmentKind.Chart:
                {
                    var chart = segment.Chart!;
                    charts.Add(chart with { X = x });
                    x += chart.Width;
                    break;
                }
                case LayoutSegmentKind.Equation:
                {
                    var equation = segment.Equation!;
                    equations.Add(equation with { X = x });
                    x += equation.Width;
                    break;
                }
            }
        }

        return layout with
        {
            Runs = runs,
            Images = images,
            Shapes = shapes,
            Charts = charts,
            Equations = equations,
            Width = x
        };
    }

    private static bool IsCjkLine(LineLayout layout)
    {
        var hasText = false;
        foreach (var run in layout.Runs)
        {
            if (run.IsTab || string.IsNullOrEmpty(run.Text))
            {
                if (run.IsTab)
                {
                    return false;
                }

                continue;
            }

            foreach (var ch in run.Text)
            {
                if (ch == ' ' || ch == '\t')
                {
                    return false;
                }

                if (!IsCjkChar(ch))
                {
                    return false;
                }

                hasText = true;
            }
        }

        return hasText;
    }

    private static bool IsCjkChar(char ch)
    {
        var code = (int)ch;
        return (code >= 0x3040 && code <= 0x30FF)
               || (code >= 0x3400 && code <= 0x4DBF)
               || (code >= 0x4E00 && code <= 0x9FFF)
               || (code >= 0xAC00 && code <= 0xD7AF)
               || (code >= 0xF900 && code <= 0xFAFF)
               || (code >= 0x2E80 && code <= 0x2FFF)
               || (code >= 0x3000 && code <= 0x303F)
               || (code >= 0x3100 && code <= 0x312F)
               || (code >= 0x31F0 && code <= 0x31FF)
               || (code >= 0xFF00 && code <= 0xFFEF);
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

    private static List<LayoutSegment> BuildLayoutSegments(LineLayout layout)
    {
        var segments = new List<LayoutSegment>(
            layout.Runs.Count + layout.Images.Count + layout.Shapes.Count + layout.Charts.Count + layout.Equations.Count);
        var order = 0;

        foreach (var run in layout.Runs)
        {
            segments.Add(new LayoutSegment(run, order++));
        }

        foreach (var image in layout.Images)
        {
            segments.Add(new LayoutSegment(image, order++));
        }

        foreach (var shape in layout.Shapes)
        {
            segments.Add(new LayoutSegment(shape, order++));
        }

        foreach (var chart in layout.Charts)
        {
            segments.Add(new LayoutSegment(chart, order++));
        }

        foreach (var equation in layout.Equations)
        {
            segments.Add(new LayoutSegment(equation, order++));
        }

        segments.Sort(static (left, right) =>
        {
            var compare = left.X.CompareTo(right.X);
            return compare != 0 ? compare : left.Order.CompareTo(right.Order);
        });

        return segments;
    }

    private enum LayoutSegmentKind
    {
        Run,
        Image,
        Shape,
        Chart,
        Equation
    }

    private readonly struct LayoutSegment
    {
        public LayoutSegmentKind Kind { get; }
        public float X { get; }
        public int Order { get; }
        public LayoutRun? Run { get; }
        public LayoutImage? Image { get; }
        public LayoutShape? Shape { get; }
        public LayoutChart? Chart { get; }
        public LayoutEquation? Equation { get; }

        public LayoutSegment(LayoutRun run, int order)
        {
            Kind = LayoutSegmentKind.Run;
            X = run.X;
            Order = order;
            Run = run;
            Image = null;
            Shape = null;
            Chart = null;
            Equation = null;
        }

        public LayoutSegment(LayoutImage image, int order)
        {
            Kind = LayoutSegmentKind.Image;
            X = image.X;
            Order = order;
            Run = null;
            Image = image;
            Shape = null;
            Chart = null;
            Equation = null;
        }

        public LayoutSegment(LayoutShape shape, int order)
        {
            Kind = LayoutSegmentKind.Shape;
            X = shape.X;
            Order = order;
            Run = null;
            Image = null;
            Shape = shape;
            Chart = null;
            Equation = null;
        }

        public LayoutSegment(LayoutChart chart, int order)
        {
            Kind = LayoutSegmentKind.Chart;
            X = chart.X;
            Order = order;
            Run = null;
            Image = null;
            Shape = null;
            Chart = chart;
            Equation = null;
        }

        public LayoutSegment(LayoutEquation equation, int order)
        {
            Kind = LayoutSegmentKind.Equation;
            X = equation.X;
            Order = order;
            Run = null;
            Image = null;
            Shape = null;
            Chart = null;
            Equation = equation;
        }
    }
}

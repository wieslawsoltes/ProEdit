using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Vibe.Office.Documents;
using Vibe.Office.Layout;

namespace Vibe.Office.Rendering.Skia;

public sealed class SkiaTextMeasurer : ITextMeasurerAdvancedSpan
{
    private bool _useHarfBuzz = true;
    private readonly HashSet<SKTypeface> _failedTypefaces = new();
    public ISkiaTypefaceResolver? TypefaceResolver { get; set; }
    private static readonly TextShapeInfo EmptyShapeInfo = new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());

    public bool UseHarfBuzz
    {
        get => _useHarfBuzz;
        set
        {
            if (_useHarfBuzz == value)
            {
                return;
            }

            _useHarfBuzz = value;
            if (value)
            {
                _failedTypefaces.Clear();
            }
        }
    }

    public TextMetrics MeasureText(string text, TextStyle style)
    {
        var value = text ?? string.Empty;
        return MeasureText(value.AsSpan(), style);
    }

    public TextMetrics MeasureText(ReadOnlySpan<char> text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        using var paint = CreatePaint(style, TypefaceResolver);
        var fallbackResolver = TypefaceResolver as ISkiaTypefaceFallbackResolver;
        var baseTypeface = paint.Typeface ?? SKTypeface.Default;
        var width = 0f;
        if (text.Length > 0)
        {
            var applyKerning = ShouldApplyKerning(style);
            var needsFallback = fallbackResolver is not null && !paint.ContainsGlyphs(text);
            if (needsFallback)
            {
                width = MeasureTextWithFallback(text, style, paint, fallbackResolver!, useShaper: _useHarfBuzz && applyKerning, applyKerning);
            }
            else if (applyKerning && CanShape(baseTypeface))
            {
                try
                {
                    using var shaper = new SKShaper(baseTypeface);
                    using var buffer = CreateBuffer(text, style);
                    var result = shaper.Shape(buffer, paint);
                    width = MathF.Abs(result.Width);
                    if (float.IsNaN(width) || float.IsInfinity(width))
                    {
                        MarkShaperFailed(baseTypeface);
                        width = paint.MeasureText(text);
                    }
                    else if (width <= 0f)
                    {
                        width = paint.MeasureText(text);
                    }
                }
                catch
                {
                    MarkShaperFailed(baseTypeface);
                    width = paint.MeasureText(text);
                }
            }
            else
            {
                width = applyKerning
                    ? paint.MeasureText(text)
                    : MeasureTextWithoutKerning(text, paint);
            }
        }

        if (float.IsNaN(width) || float.IsInfinity(width))
        {
            width = 0f;
        }
        var metrics = paint.FontMetrics;
        var ascent = -metrics.Ascent;
        var leading = MathF.Max(0f, metrics.Leading);
        var descent = metrics.Descent + leading;
        var height = ascent + descent;

        return new TextMetrics(width, height, ascent, descent);
    }

    public TextShapeInfo ShapeText(string text, TextStyle style)
    {
        var value = text ?? string.Empty;
        return ShapeText(value.AsSpan(), style);
    }

    public TextShapeInfo ShapeText(ReadOnlySpan<char> text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (text.Length == 0)
        {
            return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
        }

        using var paint = CreatePaint(style, TypefaceResolver);
        var fallbackResolver = TypefaceResolver as ISkiaTypefaceFallbackResolver;
        var baseTypeface = paint.Typeface ?? SKTypeface.Default;
        var needsFallback = fallbackResolver is not null && !paint.ContainsGlyphs(text);
        var applyKerning = ShouldApplyKerning(style);
        if (!needsFallback && CanShape(baseTypeface))
        {
            if (applyKerning && TryShapeTextSegment(text, style, paint, out var shaped))
            {
                return shaped;
            }

            if (applyKerning)
            {
                MarkShaperFailed(baseTypeface);
            }
        }

        if (needsFallback)
        {
            return ShapeTextWithFallback(text, style, paint, fallbackResolver!, _useHarfBuzz && applyKerning);
        }

        return BuildSimpleShapeInfo(text, paint);
    }

    private float MeasureTextWithFallback(
        ReadOnlySpan<char> text,
        TextStyle style,
        SKPaint basePaint,
        ISkiaTypefaceFallbackResolver fallbackResolver,
        bool useShaper,
        bool applyKerning)
    {
        var segments = BuildTypefaceSegments(text, style, basePaint, fallbackResolver);
        if (segments.Count == 0)
        {
            return 0f;
        }

        var width = 0f;
        var paintCache = new Dictionary<SKTypeface, SKPaint>();
        var shaperCache = useShaper ? new Dictionary<SKTypeface, SKShaper>() : null;

        try
        {
            foreach (var segment in segments)
            {
                var segmentSpan = text.Slice(segment.Start, segment.Length);
                var paint = GetDerivedPaint(basePaint, segment.Typeface, paintCache);

                if (useShaper)
                {
                    try
                    {
                        var shaper = TryGetShaper(segment.Typeface, shaperCache!);
                        if (shaper is not null)
                        {
                            using var buffer = CreateBuffer(segmentSpan, style);
                            var result = shaper.Shape(buffer, paint);
                            var segmentWidth = MathF.Abs(result.Width);
                            if (!float.IsNaN(segmentWidth) && !float.IsInfinity(segmentWidth))
                            {
                                width += segmentWidth;
                                continue;
                            }

                            MarkShaperFailed(segment.Typeface);
                        }
                    }
                    catch
                    {
                        MarkShaperFailed(segment.Typeface);
                    }
                }

                width += applyKerning
                    ? paint.MeasureText(segmentSpan)
                    : MeasureTextWithoutKerning(segmentSpan, paint);
            }
        }
        finally
        {
            foreach (var paint in paintCache.Values)
            {
                paint.Dispose();
            }

            if (shaperCache is not null)
            {
                foreach (var shaper in shaperCache.Values)
                {
                    shaper.Dispose();
                }
            }
        }

        return width;
    }

    private static float MeasureTextWithoutKerning(ReadOnlySpan<char> text, SKPaint paint)
    {
        if (text.IsEmpty)
        {
            return 0f;
        }

        var width = 0f;
        var index = 0;
        while (index < text.Length)
        {
            var length = TextCluster.GetNextClusterLength(text, index);
            if (length <= 0)
            {
                break;
            }

            width += paint.MeasureText(text.Slice(index, length));
            index += length;
        }

        return width;
    }

    private TextShapeInfo ShapeTextWithFallback(ReadOnlySpan<char> text, TextStyle style, SKPaint basePaint, ISkiaTypefaceFallbackResolver fallbackResolver, bool useShaper)
    {
        var segments = BuildTypefaceSegments(text, style, basePaint, fallbackResolver);
        if (segments.Count == 0)
        {
            return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
        }

        var offsets = new List<int>();
        var advances = new List<float>();
        var paintCache = new Dictionary<SKTypeface, SKPaint>();
        var shaperCache = useShaper ? new Dictionary<SKTypeface, SKShaper>() : null;

        try
        {
            foreach (var segment in segments)
            {
                var segmentSpan = text.Slice(segment.Start, segment.Length);
                var paint = GetDerivedPaint(basePaint, segment.Typeface, paintCache);

                TextShapeInfo segmentShape;
                if (useShaper)
                {
                    try
                    {
                        var shaper = TryGetShaper(segment.Typeface, shaperCache!);
                        if (shaper is not null)
                        {
                            if (TryShapeTextSegment(segmentSpan, style, paint, shaper, out segmentShape))
                            {
                                AppendShapeInfo(segmentShape, segment.Start, offsets, advances);
                                continue;
                            }

                            MarkShaperFailed(segment.Typeface);
                        }
                    }
                    catch
                    {
                        MarkShaperFailed(segment.Typeface);
                    }
                }

                segmentShape = BuildSimpleShapeInfo(segmentSpan, paint);
                AppendShapeInfo(segmentShape, segment.Start, offsets, advances);
            }
        }
        finally
        {
            foreach (var paint in paintCache.Values)
            {
                paint.Dispose();
            }

            if (shaperCache is not null)
            {
                foreach (var shaper in shaperCache.Values)
                {
                    shaper.Dispose();
                }
            }
        }

        return offsets.Count == 0
            ? new TextShapeInfo(text.Length, Array.Empty<int>(), Array.Empty<float>())
            : new TextShapeInfo(text.Length, offsets.ToArray(), advances.ToArray());
    }

    private static void AppendShapeInfo(TextShapeInfo segmentShape, int offsetBase, List<int> offsets, List<float> advances)
    {
        if (segmentShape.ClusterOffsets.Length == 0)
        {
            return;
        }

        for (var i = 0; i < segmentShape.ClusterOffsets.Length; i++)
        {
            offsets.Add(segmentShape.ClusterOffsets[i] + offsetBase);
            var advance = i < segmentShape.ClusterAdvances.Length ? segmentShape.ClusterAdvances[i] : 0f;
            advances.Add(advance);
        }
    }

    internal static bool TryShapeTextSegment(ReadOnlySpan<char> text, TextStyle style, SKPaint paint, out TextShapeInfo shape)
    {
        shape = EmptyShapeInfo;
        try
        {
            using var shaper = new SKShaper(paint.Typeface ?? SKTypeface.Default);
            return TryShapeTextSegment(text, style, paint, shaper, out shape);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryShapeTextSegment(ReadOnlySpan<char> text, TextStyle style, SKPaint paint, SKShaper shaper, out TextShapeInfo shape)
    {
        shape = EmptyShapeInfo;
        using var buffer = CreateBuffer(text, style);
        var result = shaper.Shape(buffer, paint);
        var shaped = BuildShapeInfo(text.Length, result);
        if (shaped.ClusterOffsets.Length == 0 || shaped.ClusterOffsets.Length != shaped.ClusterAdvances.Length)
        {
            return false;
        }

        shape = shaped;
        return true;
    }

    internal static TextShapeInfo BuildSimpleShapeInfo(ReadOnlySpan<char> text, SKPaint paint)
    {
        if (text.IsEmpty)
        {
            return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
        }

        var offsets = new List<int>();
        var advances = new List<float>();
        var index = 0;
        while (index < text.Length)
        {
            var clusterLength = GetNextClusterLength(text, index);
            offsets.Add(index);
            advances.Add(paint.MeasureText(text.Slice(index, clusterLength)));
            index += clusterLength;
        }

        return offsets.Count == 0
            ? new TextShapeInfo(text.Length, Array.Empty<int>(), Array.Empty<float>())
            : new TextShapeInfo(text.Length, offsets.ToArray(), advances.ToArray());
    }

    internal readonly record struct TypefaceSegment(int Start, int Length, SKTypeface Typeface);

    internal static List<TypefaceSegment> BuildTypefaceSegments(
        ReadOnlySpan<char> text,
        TextStyle style,
        SKPaint basePaint,
        ISkiaTypefaceFallbackResolver? fallbackResolver)
    {
        var segments = new List<TypefaceSegment>();
        if (text.IsEmpty)
        {
            return segments;
        }

        var baseTypeface = basePaint.Typeface ?? SKTypeface.Default;
        if (fallbackResolver is null || basePaint.ContainsGlyphs(text))
        {
            segments.Add(new TypefaceSegment(0, text.Length, baseTypeface));
            return segments;
        }

        var index = 0;
        while (index < text.Length)
        {
            var clusterLength = GetNextClusterLength(text, index);
            var clusterSpan = text.Slice(index, clusterLength);
            var typeface = baseTypeface;
            if (!basePaint.ContainsGlyphs(clusterSpan))
            {
                var fallback = fallbackResolver.ResolveFallbackTypeface(style, clusterSpan);
                if (fallback is not null)
                {
                    typeface = fallback;
                }
            }

            if (segments.Count > 0 && ReferenceEquals(segments[^1].Typeface, typeface))
            {
                var last = segments[^1];
                segments[^1] = last with { Length = last.Length + clusterLength };
            }
            else
            {
                segments.Add(new TypefaceSegment(index, clusterLength, typeface));
            }

            index += clusterLength;
        }

        return segments;
    }

    private static int GetNextClusterLength(ReadOnlySpan<char> text, int start)
    {
        return TextCluster.GetNextClusterLength(text, start);
    }

    private static SKPaint GetDerivedPaint(SKPaint basePaint, SKTypeface typeface, Dictionary<SKTypeface, SKPaint> cache)
    {
        var baseTypeface = basePaint.Typeface ?? SKTypeface.Default;
        if (ReferenceEquals(typeface, baseTypeface))
        {
            return basePaint;
        }

        if (cache.TryGetValue(typeface, out var cached))
        {
            return cached;
        }

        var derived = CreateDerivedPaint(basePaint, typeface);
        cache[typeface] = derived;
        return derived;
    }

    private static SKPaint CreateDerivedPaint(SKPaint basePaint, SKTypeface typeface)
    {
        return new SKPaint
        {
            Typeface = typeface,
            TextSize = basePaint.TextSize,
            IsAntialias = basePaint.IsAntialias,
            SubpixelText = basePaint.SubpixelText,
            LcdRenderText = basePaint.LcdRenderText,
            HintingLevel = basePaint.HintingLevel,
            FilterQuality = basePaint.FilterQuality,
            Color = basePaint.Color
        };
    }

    private SKShaper? TryGetShaper(SKTypeface typeface, Dictionary<SKTypeface, SKShaper> cache)
    {
        if (!CanShape(typeface))
        {
            return null;
        }

        if (cache.TryGetValue(typeface, out var cached))
        {
            return cached;
        }

        try
        {
            var shaper = new SKShaper(typeface);
            cache[typeface] = shaper;
            return shaper;
        }
        catch
        {
            MarkShaperFailed(typeface);
            return null;
        }
    }

    internal static TextShapeInfo BuildShapeInfo(int textLength, SKShaper.Result result)
    {
        if (textLength <= 0)
        {
            return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
        }

        var clusters = result.Clusters;
        var points = result.Points;
        if (clusters is null || points is null)
        {
            return new TextShapeInfo(textLength, Array.Empty<int>(), Array.Empty<float>());
        }

        var glyphCount = Math.Min(clusters.Length, points.Length);
        if (glyphCount == 0)
        {
            return new TextShapeInfo(textLength, Array.Empty<int>(), Array.Empty<float>());
        }

        var totalWidth = MathF.Abs(result.Width);
        if (float.IsNaN(totalWidth) || float.IsInfinity(totalWidth) || totalWidth <= 0f)
        {
            var lastX = points[glyphCount - 1].X;
            totalWidth = float.IsNaN(lastX) || float.IsInfinity(lastX) ? 0f : MathF.Abs(lastX);
        }

        var glyphAdvances = new float[glyphCount];
        for (var i = 0; i < glyphCount; i++)
        {
            var currentX = points[i].X;
            var nextX = i + 1 < glyphCount ? points[i + 1].X : totalWidth;
            if (float.IsNaN(currentX) || float.IsNaN(nextX) || float.IsInfinity(currentX) || float.IsInfinity(nextX))
            {
                return new TextShapeInfo(textLength, Array.Empty<int>(), Array.Empty<float>());
            }

            var advance = nextX - currentX;
            glyphAdvances[i] = advance < 0f ? -advance : advance;
        }

        var clusterAdvances = new Dictionary<int, float>();
        var maxOffset = Math.Max(0, textLength - 1);
        for (var i = 0; i < glyphCount; i++)
        {
            var clusterIndex = (int)clusters[i];
            if (clusterIndex < 0)
            {
                clusterIndex = 0;
            }
            else if (clusterIndex > maxOffset)
            {
                clusterIndex = maxOffset;
            }

            if (!clusterAdvances.TryGetValue(clusterIndex, out var advance))
            {
                advance = 0f;
            }

            clusterAdvances[clusterIndex] = advance + glyphAdvances[i];
        }

        var offsets = clusterAdvances.Keys.ToArray();
        Array.Sort(offsets);
        var advances = new float[offsets.Length];
        for (var i = 0; i < offsets.Length; i++)
        {
            advances[i] = clusterAdvances[offsets[i]];
        }

        return new TextShapeInfo(textLength, offsets, advances);
    }

    internal static HarfBuzzSharp.Buffer CreateBuffer(ReadOnlySpan<char> text, TextStyle style)
    {
        var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(text);
        if (!string.IsNullOrWhiteSpace(style.Language))
        {
            buffer.Language = new HarfBuzzSharp.Language(style.Language);
        }
        buffer.GuessSegmentProperties();
        var direction = TextBidi.FindFirstStrongDirection(text);
        buffer.Direction = direction == BidiDirection.Rtl
            ? HarfBuzzSharp.Direction.RightToLeft
            : HarfBuzzSharp.Direction.LeftToRight;
        return buffer;
    }

    internal static bool ShouldApplyKerning(TextStyle style)
    {
        if (!style.Kerning.HasValue)
        {
            return true;
        }

        var threshold = style.Kerning.Value;
        if (threshold <= 0f)
        {
            return true;
        }

        return style.FontSize >= threshold;
    }

    private bool CanShape(SKTypeface? typeface)
    {
        if (!_useHarfBuzz)
        {
            return false;
        }

        var resolved = typeface ?? SKTypeface.Default;
        return !_failedTypefaces.Contains(resolved);
    }

    private void MarkShaperFailed(SKTypeface typeface)
    {
        _failedTypefaces.Add(typeface);
    }

    internal static SKPaint CreatePaint(TextStyle style, ISkiaTypefaceResolver? typefaceResolver = null)
    {
        var weight = style.FontWeight == DocFontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = style.FontStyle == DocFontStyle.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var typeface = typefaceResolver?.ResolveTypeface(style)
                       ?? SKTypeface.FromFamilyName(style.FontFamily, weight, SKFontStyleWidth.Normal, slant);

        return new SKPaint
        {
            Typeface = typeface,
            TextSize = style.FontSize,
            TextScaleX = style.HorizontalScale > 0f ? style.HorizontalScale : 1f,
            IsAntialias = true,
            SubpixelText = true,
            LcdRenderText = true,
            HintingLevel = SKPaintHinting.Full,
            FilterQuality = SKFilterQuality.High
        };
    }
}

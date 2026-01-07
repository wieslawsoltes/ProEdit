using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Vibe.Office.Documents;
using Vibe.Office.Layout;

namespace Vibe.Office.Rendering.Skia;

public sealed class SkiaTextMeasurer : ITextMeasurerAdvancedSpan
{
    private bool _disableShaping;
    private bool _useHarfBuzz = true;
    public ISkiaTypefaceResolver? TypefaceResolver { get; set; }

    public bool UseHarfBuzz
    {
        get => _useHarfBuzz;
        set => _useHarfBuzz = value;
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
        var width = 0f;
        if (text.Length > 0 && _useHarfBuzz && !_disableShaping)
        {
            var needsFallback = fallbackResolver is not null && !paint.ContainsGlyphs(text);
            if (needsFallback)
            {
                width = MeasureTextWithFallback(text, style, paint, fallbackResolver!, useShaper: true);
            }
            else
            {
                try
                {
                    using var shaper = new SKShaper(paint.Typeface ?? SKTypeface.Default);
                    using var buffer = CreateBuffer(text, style);
                    var result = shaper.Shape(buffer, paint);
                    width = MathF.Abs(result.Width);
                    if (float.IsNaN(width) || float.IsInfinity(width) || width <= 0f)
                    {
                        _disableShaping = true;
                        width = paint.MeasureText(text);
                    }
                }
                catch
                {
                    _disableShaping = true;
                    width = paint.MeasureText(text);
                }
            }
        }
        else if (text.Length > 0)
        {
            var needsFallback = fallbackResolver is not null && !paint.ContainsGlyphs(text);
            width = needsFallback
                ? MeasureTextWithFallback(text, style, paint, fallbackResolver!, useShaper: false)
                : paint.MeasureText(text);
        }

        if (float.IsNaN(width) || float.IsInfinity(width))
        {
            width = 0f;
        }
        var metrics = paint.FontMetrics;
        var ascent = -metrics.Ascent;
        var descent = metrics.Descent;
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
        var needsFallback = fallbackResolver is not null && !paint.ContainsGlyphs(text);
        if (!needsFallback && _useHarfBuzz && !_disableShaping)
        {
            if (TryShapeTextSegment(text, style, paint, out var shaped))
            {
                return shaped;
            }

            _disableShaping = true;
        }

        if (needsFallback)
        {
            return ShapeTextWithFallback(text, style, paint, fallbackResolver!, _useHarfBuzz && !_disableShaping);
        }

        return BuildSimpleShapeInfo(text, paint);
    }

    private float MeasureTextWithFallback(ReadOnlySpan<char> text, TextStyle style, SKPaint basePaint, ISkiaTypefaceFallbackResolver fallbackResolver, bool useShaper)
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

                if (useShaper && !_disableShaping)
                {
                    try
                    {
                        var shaper = GetShaper(segment.Typeface, shaperCache!);
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
                        }
                    }
                    catch
                    {
                        _disableShaping = true;
                    }
                }

                width += paint.MeasureText(segmentSpan);
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
                if (useShaper && !_disableShaping)
                {
                    try
                    {
                        var shaper = GetShaper(segment.Typeface, shaperCache!);
                        if (shaper is not null && TryShapeTextSegment(segmentSpan, style, paint, shaper, out segmentShape))
                        {
                            AppendShapeInfo(segmentShape, segment.Start, offsets, advances);
                            continue;
                        }
                    }
                    catch
                    {
                        _disableShaping = true;
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
        shape = default;
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
        shape = default;
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
        if ((uint)start >= (uint)text.Length)
        {
            return 0;
        }

        if (!Rune.TryDecodeFromUtf16(text[start..], out var rune, out var consumed))
        {
            rune = new Rune(text[start]);
            consumed = 1;
        }

        var index = start + consumed;
        var pendingJoiner = false;
        var lastWasRegionalIndicator = IsRegionalIndicator(rune);

        while (index < text.Length)
        {
            if (!Rune.TryDecodeFromUtf16(text[index..], out var nextRune, out var nextConsumed))
            {
                nextRune = new Rune(text[index]);
                nextConsumed = 1;
            }

            if (pendingJoiner)
            {
                index += nextConsumed;
                pendingJoiner = false;
                lastWasRegionalIndicator = IsRegionalIndicator(nextRune);
                continue;
            }

            if (IsZeroWidthJoiner(nextRune))
            {
                index += nextConsumed;
                pendingJoiner = true;
                lastWasRegionalIndicator = false;
                continue;
            }

            if (IsCombiningMark(nextRune) || IsVariationSelector(nextRune) || IsEmojiModifier(nextRune) || IsEmojiTag(nextRune))
            {
                index += nextConsumed;
                continue;
            }

            if (IsRegionalIndicator(nextRune) && lastWasRegionalIndicator)
            {
                index += nextConsumed;
                lastWasRegionalIndicator = false;
                continue;
            }

            break;
        }

        return Math.Max(1, index - start);
    }

    private static bool IsCombiningMark(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category == UnicodeCategory.NonSpacingMark
               || category == UnicodeCategory.SpacingCombiningMark
               || category == UnicodeCategory.EnclosingMark;
    }

    private static bool IsVariationSelector(Rune rune)
    {
        var code = rune.Value;
        return (code >= 0xFE00 && code <= 0xFE0F)
               || (code >= 0xE0100 && code <= 0xE01EF);
    }

    private static bool IsEmojiModifier(Rune rune)
    {
        var code = rune.Value;
        return code >= 0x1F3FB && code <= 0x1F3FF;
    }

    private static bool IsZeroWidthJoiner(Rune rune) => rune.Value == 0x200D;

    private static bool IsRegionalIndicator(Rune rune)
    {
        var code = rune.Value;
        return code >= 0x1F1E6 && code <= 0x1F1FF;
    }

    private static bool IsEmojiTag(Rune rune)
    {
        var code = rune.Value;
        return code >= 0xE0020 && code <= 0xE007F;
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

    private static SKShaper? GetShaper(SKTypeface typeface, Dictionary<SKTypeface, SKShaper> cache)
    {
        if (cache.TryGetValue(typeface, out var cached))
        {
            return cached;
        }

        var shaper = new SKShaper(typeface);
        cache[typeface] = shaper;
        return shaper;
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
        if (HasRtlCharacters(text))
        {
            buffer.Direction = HarfBuzzSharp.Direction.RightToLeft;
        }
        return buffer;
    }

    private static bool HasRtlCharacters(ReadOnlySpan<char> text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!System.Text.Rune.TryDecodeFromUtf16(text[index..], out var rune, out var consumed))
            {
                rune = new System.Text.Rune(text[index]);
                consumed = 1;
            }

            var code = rune.Value;
            if ((code >= 0x0590 && code <= 0x08FF)
                || (code >= 0xFB1D && code <= 0xFDFF)
                || (code >= 0xFE70 && code <= 0xFEFF))
            {
                return true;
            }

            index += consumed;
        }

        return false;
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
            IsAntialias = true,
            SubpixelText = true,
            LcdRenderText = true,
            HintingLevel = SKPaintHinting.Full,
            FilterQuality = SKFilterQuality.High
        };
    }
}

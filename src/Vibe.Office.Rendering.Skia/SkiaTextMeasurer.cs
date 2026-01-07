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
        var width = 0f;
        if (text.Length > 0 && _useHarfBuzz && !_disableShaping)
        {
            try
            {
                using var shaper = new SKShaper(paint.Typeface ?? SKTypeface.Default);
                using var buffer = new HarfBuzzSharp.Buffer();
                buffer.AddUtf16(text);
                buffer.GuessSegmentProperties();
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
        else if (text.Length > 0)
        {
            width = paint.MeasureText(text);
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
        if (_useHarfBuzz && !_disableShaping)
        {
            try
            {
                using var shaper = new SKShaper(paint.Typeface ?? SKTypeface.Default);
                using var buffer = new HarfBuzzSharp.Buffer();
                buffer.AddUtf16(text);
                buffer.GuessSegmentProperties();
                var result = shaper.Shape(buffer, paint);
                var shaped = BuildShapeInfo(text.Length, result);
                if (shaped.ClusterOffsets.Length > 0)
                {
                    return shaped;
                }
            }
            catch
            {
                _disableShaping = true;
            }
        }

        var offsets = new int[text.Length];
        var advances = new float[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            offsets[i] = i;
            advances[i] = paint.MeasureText(text.Slice(i, 1));
        }

        return new TextShapeInfo(text.Length, offsets, advances);
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

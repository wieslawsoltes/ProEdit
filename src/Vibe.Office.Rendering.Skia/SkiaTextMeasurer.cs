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
    private const int FontSizeScale = 512;
    private const int MaxFeatureCount = 40;
    private const float VerticalCompressionScale = 0.85f; // Word-like vertical compression factor.
    private static readonly HarfBuzzSharp.Tag LigaTag = new HarfBuzzSharp.Tag('l', 'i', 'g', 'a');
    private static readonly HarfBuzzSharp.Tag CligTag = new HarfBuzzSharp.Tag('c', 'l', 'i', 'g');
    private static readonly HarfBuzzSharp.Tag DligTag = new HarfBuzzSharp.Tag('d', 'l', 'i', 'g');
    private static readonly HarfBuzzSharp.Tag HligTag = new HarfBuzzSharp.Tag('h', 'l', 'i', 'g');
    private static readonly HarfBuzzSharp.Tag CaltTag = new HarfBuzzSharp.Tag('c', 'a', 'l', 't');
    private static readonly HarfBuzzSharp.Tag PnumTag = new HarfBuzzSharp.Tag('p', 'n', 'u', 'm');
    private static readonly HarfBuzzSharp.Tag TnumTag = new HarfBuzzSharp.Tag('t', 'n', 'u', 'm');
    private static readonly HarfBuzzSharp.Tag LnumTag = new HarfBuzzSharp.Tag('l', 'n', 'u', 'm');
    private static readonly HarfBuzzSharp.Tag OnumTag = new HarfBuzzSharp.Tag('o', 'n', 'u', 'm');
    private static readonly HarfBuzzSharp.Tag KernTag = new HarfBuzzSharp.Tag('k', 'e', 'r', 'n');
    private static readonly HarfBuzzSharp.Tag VertTag = new HarfBuzzSharp.Tag('v', 'e', 'r', 't');
    private static readonly HarfBuzzSharp.Tag Vrt2Tag = new HarfBuzzSharp.Tag('v', 'r', 't', '2');
    private static readonly HarfBuzzSharp.Tag VkrnTag = new HarfBuzzSharp.Tag('v', 'k', 'r', 'n');
    private static readonly HarfBuzzSharp.Tag VknaTag = new HarfBuzzSharp.Tag('v', 'k', 'n', 'a');

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
        var advanceScale = ResolveVerticalAdvanceScale(style);
        var applyAdvanceScale = advanceScale != 1f;
        if (text.Length > 0)
        {
            var applyKerning = ShouldApplyKerning(style);
            var useOpenTypeFeatures = HasOpenTypeFeatures(style);
            var needsFallback = fallbackResolver is not null && !paint.ContainsGlyphs(text);
            if (needsFallback)
            {
                width = MeasureTextWithFallback(text, style, paint, fallbackResolver!, useShaper: _useHarfBuzz && (applyKerning || useOpenTypeFeatures), applyKerning, useOpenTypeFeatures);
            }
            else if (useOpenTypeFeatures && CanShape(baseTypeface))
            {
                if (TryShapeTextWithFeatures(text, style, paint, baseTypeface, applyKerning, out _, out var shapedWidth))
                {
                    width = shapedWidth;
                }
                else
                {
                    MarkShaperFailed(baseTypeface);
                    width = applyKerning
                        ? paint.MeasureText(text)
                        : MeasureTextWithoutKerning(text, paint);
                }
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
                    if (applyAdvanceScale)
                    {
                        width *= advanceScale;
                    }
                }
                catch
                {
                    MarkShaperFailed(baseTypeface);
                    width = paint.MeasureText(text);
                    if (applyAdvanceScale)
                    {
                        width *= advanceScale;
                    }
                }
            }
            else
            {
                width = applyKerning
                    ? paint.MeasureText(text)
                    : MeasureTextWithoutKerning(text, paint);
                if (applyAdvanceScale)
                {
                    width *= advanceScale;
                }
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
        var useOpenTypeFeatures = HasOpenTypeFeatures(style);
        if (!needsFallback && CanShape(baseTypeface))
        {
            if (useOpenTypeFeatures && TryShapeTextWithFeatures(text, style, paint, baseTypeface, applyKerning, out var shapedWithFeatures, out _))
            {
                return shapedWithFeatures;
            }

            if (!useOpenTypeFeatures && applyKerning && TryShapeTextSegment(text, style, paint, out var shapedWithKerning))
            {
                return shapedWithKerning;
            }

            if (applyKerning || useOpenTypeFeatures)
            {
                MarkShaperFailed(baseTypeface);
            }
        }

        if (needsFallback)
        {
            return ShapeTextWithFallback(text, style, paint, fallbackResolver!, _useHarfBuzz && (applyKerning || useOpenTypeFeatures), useOpenTypeFeatures, applyKerning);
        }

        return BuildSimpleShapeInfo(text, paint, style);
    }

    private float MeasureTextWithFallback(
        ReadOnlySpan<char> text,
        TextStyle style,
        SKPaint basePaint,
        ISkiaTypefaceFallbackResolver fallbackResolver,
        bool useShaper,
        bool applyKerning,
        bool useOpenTypeFeatures)
    {
        var segments = BuildTypefaceSegments(text, style, basePaint, fallbackResolver);
        if (segments.Count == 0)
        {
            return 0f;
        }

        var width = 0f;
        var advanceScale = ResolveVerticalAdvanceScale(style);
        var applyAdvanceScale = advanceScale != 1f;
        var paintCache = new Dictionary<SKTypeface, SKPaint>();
        var shaperCache = useShaper ? new Dictionary<SKTypeface, SKShaper>() : null;

        try
        {
            foreach (var segment in segments)
            {
                var segmentSpan = text.Slice(segment.Start, segment.Length);
                var paint = GetDerivedPaint(basePaint, segment.Typeface, paintCache);

                if (useOpenTypeFeatures)
                {
                    if (TryShapeTextWithFeatures(segmentSpan, style, paint, segment.Typeface, applyKerning, out _, out var segmentWidth))
                    {
                        width += segmentWidth;
                        continue;
                    }
                }

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
                                if (applyAdvanceScale)
                                {
                                    segmentWidth *= advanceScale;
                                }
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

                var segmentWidthFallback = applyKerning
                    ? paint.MeasureText(segmentSpan)
                    : MeasureTextWithoutKerning(segmentSpan, paint);
                if (applyAdvanceScale)
                {
                    segmentWidthFallback *= advanceScale;
                }
                width += segmentWidthFallback;
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

    private TextShapeInfo ShapeTextWithFallback(
        ReadOnlySpan<char> text,
        TextStyle style,
        SKPaint basePaint,
        ISkiaTypefaceFallbackResolver fallbackResolver,
        bool useShaper,
        bool useOpenTypeFeatures,
        bool applyKerning)
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
                if (useOpenTypeFeatures && TryShapeTextWithFeatures(segmentSpan, style, paint, segment.Typeface, applyKerning, out segmentShape, out _))
                {
                    AppendShapeInfo(segmentShape, segment.Start, offsets, advances);
                    continue;
                }

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

                segmentShape = BuildSimpleShapeInfo(segmentSpan, paint, style);
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
        var shaped = BuildShapeInfo(text.Length, result, ResolveVerticalAdvanceScale(style));
        if (shaped.ClusterOffsets.Length == 0 || shaped.ClusterOffsets.Length != shaped.ClusterAdvances.Length)
        {
            return false;
        }

        shape = shaped;
        return true;
    }

    internal static bool HasOpenTypeFeatures(TextStyle style)
    {
        return style.OpenTypeFeatures?.HasValues == true || HasVerticalLayout(style);
    }

    internal static bool TryShapeTextWithFeatures(
        ReadOnlySpan<char> text,
        TextStyle style,
        SKPaint paint,
        SKTypeface typeface,
        bool applyKerning,
        out TextShapeInfo shape,
        out float width)
    {
        shape = EmptyShapeInfo;
        width = 0f;
        if (text.IsEmpty)
        {
            return true;
        }

        if (!TryCreateHarfBuzzFont(typeface, out var fontContext))
        {
            return false;
        }

        var advanceScale = ResolveVerticalAdvanceScale(style);
        using (fontContext)
        using (var buffer = CreateBuffer(text, style))
        {
            var featureList = BuildOpenTypeFeatures(style, applyKerning, HasVerticalLayout(style));
            fontContext.Font.Shape(buffer, featureList);

            var glyphInfos = buffer.GetGlyphInfoSpan();
            var glyphPositions = buffer.GetGlyphPositionSpan();
            if (glyphInfos.IsEmpty || glyphPositions.IsEmpty)
            {
                return false;
            }

            var scaleY = paint.TextSize / FontSizeScale;
            var scaleX = scaleY * paint.TextScaleX;
            shape = BuildShapeInfoFromGlyphs(text.Length, glyphInfos, glyphPositions, scaleX, advanceScale, out width);
            return shape.ClusterOffsets.Length != 0 && shape.ClusterOffsets.Length == shape.ClusterAdvances.Length;
        }
    }

    internal static bool TryGetHarfBuzzGlyphRun(
        ReadOnlySpan<char> text,
        TextStyle style,
        SKPaint paint,
        bool applyKerning,
        out HarfBuzzGlyphRun run)
    {
        run = default;
        if (text.IsEmpty)
        {
            return false;
        }

        if (!HasOpenTypeFeatures(style))
        {
            return false;
        }

        var typeface = paint.Typeface ?? SKTypeface.Default;
        if (!TryCreateHarfBuzzFont(typeface, out var fontContext))
        {
            return false;
        }

        var advanceScale = ResolveVerticalAdvanceScale(style);
        using (fontContext)
        using (var buffer = CreateBuffer(text, style))
        {
            var featureList = BuildOpenTypeFeatures(style, applyKerning, HasVerticalLayout(style));
            fontContext.Font.Shape(buffer, featureList);

            var glyphInfos = buffer.GetGlyphInfoSpan();
            var glyphPositions = buffer.GetGlyphPositionSpan();
            var glyphCount = Math.Min(glyphInfos.Length, glyphPositions.Length);
            if (glyphCount == 0)
            {
                return false;
            }

            var scaleY = paint.TextSize / FontSizeScale;
            var scaleX = scaleY * paint.TextScaleX;
            var glyphs = new ushort[glyphCount];
            var positions = new SKPoint[glyphCount];
            var clusters = new uint[glyphCount];
            var x = 0f;
            var y = 0f;

            for (var i = 0; i < glyphCount; i++)
            {
                var info = glyphInfos[i];
                var codepoint = info.Codepoint;
                if (codepoint > ushort.MaxValue)
                {
                    return false;
                }

                glyphs[i] = (ushort)codepoint;
                clusters[i] = info.Cluster;

                var pos = glyphPositions[i];
                var xOffset = pos.XOffset * scaleX;
                if (advanceScale != 1f)
                {
                    xOffset *= advanceScale;
                }
                positions[i] = new SKPoint(
                    x + xOffset,
                    y - pos.YOffset * scaleY);
                var xAdvance = pos.XAdvance * scaleX;
                if (advanceScale != 1f)
                {
                    xAdvance *= advanceScale;
                }
                x += xAdvance;
                y += pos.YAdvance * scaleY;
            }

            var shapeInfo = BuildShapeInfoFromGlyphs(text.Length, glyphInfos, glyphPositions, scaleX, advanceScale, out var width);
            if (shapeInfo.ClusterOffsets.Length == 0 || shapeInfo.ClusterOffsets.Length != shapeInfo.ClusterAdvances.Length)
            {
                return false;
            }

            run = new HarfBuzzGlyphRun(glyphs, positions, clusters, shapeInfo, width);
            return true;
        }
    }

    private static TextShapeInfo BuildShapeInfoFromGlyphs(
        int textLength,
        ReadOnlySpan<HarfBuzzSharp.GlyphInfo> glyphInfos,
        ReadOnlySpan<HarfBuzzSharp.GlyphPosition> glyphPositions,
        float scaleX,
        float advanceScale,
        out float width)
    {
        width = 0f;
        if (textLength <= 0)
        {
            return new TextShapeInfo(textLength, Array.Empty<int>(), Array.Empty<float>());
        }

        var glyphCount = Math.Min(glyphInfos.Length, glyphPositions.Length);
        if (glyphCount == 0)
        {
            return new TextShapeInfo(textLength, Array.Empty<int>(), Array.Empty<float>());
        }

        var clusterAdvances = new Dictionary<int, float>();
        var maxOffset = Math.Max(0, textLength - 1);
        var total = 0f;
        for (var i = 0; i < glyphCount; i++)
        {
            var advance = glyphPositions[i].XAdvance * scaleX;
            if (advanceScale != 1f)
            {
                advance *= advanceScale;
            }
            total += advance;

            var clusterIndex = (int)glyphInfos[i].Cluster;
            if (clusterIndex < 0)
            {
                clusterIndex = 0;
            }
            else if (clusterIndex > maxOffset)
            {
                clusterIndex = maxOffset;
            }

            if (!clusterAdvances.TryGetValue(clusterIndex, out var current))
            {
                current = 0f;
            }

            clusterAdvances[clusterIndex] = current + MathF.Abs(advance);
        }

        width = MathF.Abs(total);

        var offsets = clusterAdvances.Keys.ToArray();
        Array.Sort(offsets);
        var advances = new float[offsets.Length];
        for (var i = 0; i < offsets.Length; i++)
        {
            advances[i] = clusterAdvances[offsets[i]];
        }

        return new TextShapeInfo(textLength, offsets, advances);
    }

    private static bool TryCreateHarfBuzzFont(SKTypeface typeface, out HarfBuzzFontContext context)
    {
        context = default;
        SKStreamAsset? stream = null;
        HarfBuzzSharp.Blob? blob = null;
        HarfBuzzSharp.Face? face = null;
        HarfBuzzSharp.Font? font = null;
        try
        {
            stream = typeface.OpenStream(out var ttcIndex);
            if (stream is null)
            {
                return false;
            }

            var streamOwnedByBlob = stream.GetMemoryBase() != IntPtr.Zero;
            blob = stream.ToHarfBuzzBlob();
            if (streamOwnedByBlob)
            {
                stream = null;
            }
            else
            {
                stream.Dispose();
                stream = null;
            }

            face = new HarfBuzzSharp.Face(blob, ttcIndex);
            face.Index = ttcIndex;
            face.UnitsPerEm = typeface.UnitsPerEm;

            font = new HarfBuzzSharp.Font(face);
            font.SetScale(FontSizeScale, FontSizeScale);
            font.SetFunctionsOpenType();

            context = new HarfBuzzFontContext(font, face, blob);
            return true;
        }
        catch
        {
            font?.Dispose();
            face?.Dispose();
            blob?.Dispose();
            stream?.Dispose();
            return false;
        }
    }

    private static HarfBuzzSharp.Feature[] BuildOpenTypeFeatures(TextStyle style, bool applyKerning, bool includeVerticalFeatures)
    {
        Span<HarfBuzzSharp.Feature> buffer = stackalloc HarfBuzzSharp.Feature[MaxFeatureCount];
        var count = 0;
        var features = style.OpenTypeFeatures;
        if (features is not null)
        {
            if (features.Ligatures.HasValue)
            {
                var ligatures = features.Ligatures.Value;
                count = AppendFeature(buffer, count, LigaTag, ligatures.HasFlag(DocLigatureOptions.Standard) ? 1u : 0u);
                count = AppendFeature(buffer, count, CligTag, ligatures.HasFlag(DocLigatureOptions.Contextual) ? 1u : 0u);
                count = AppendFeature(buffer, count, DligTag, ligatures.HasFlag(DocLigatureOptions.Discretional) ? 1u : 0u);
                count = AppendFeature(buffer, count, HligTag, ligatures.HasFlag(DocLigatureOptions.Historical) ? 1u : 0u);
            }

            if (features.ContextualAlternates.HasValue)
            {
                count = AppendFeature(buffer, count, CaltTag, features.ContextualAlternates.Value ? 1u : 0u);
            }

            if (features.NumberForm.HasValue)
            {
                var form = features.NumberForm.Value;
                var lining = form == DocNumberForm.Lining ? 1u : 0u;
                var oldStyle = form == DocNumberForm.OldStyle ? 1u : 0u;
                count = AppendFeature(buffer, count, LnumTag, lining);
                count = AppendFeature(buffer, count, OnumTag, oldStyle);
            }

            if (features.NumberSpacing.HasValue)
            {
                var spacing = features.NumberSpacing.Value;
                var proportional = spacing == DocNumberSpacing.Proportional ? 1u : 0u;
                var tabular = spacing == DocNumberSpacing.Tabular ? 1u : 0u;
                count = AppendFeature(buffer, count, PnumTag, proportional);
                count = AppendFeature(buffer, count, TnumTag, tabular);
            }

            if (features.StylisticSets.HasValue)
            {
                var sets = features.StylisticSets.Value;
                if (sets == 0u)
                {
                    for (var setIndex = 1; setIndex <= 20; setIndex++)
                    {
                        count = AppendFeature(buffer, count, CreateStylisticTag(setIndex), 0u);
                    }
                }
                else
                {
                    for (var setIndex = 1; setIndex <= 20; setIndex++)
                    {
                        if ((sets & (1u << (setIndex - 1))) == 0)
                        {
                            continue;
                        }

                        count = AppendFeature(buffer, count, CreateStylisticTag(setIndex), 1u);
                    }
                }
            }
        }

        if (!applyKerning)
        {
            count = AppendFeature(buffer, count, KernTag, 0u);
        }

        if (includeVerticalFeatures)
        {
            count = AppendFeature(buffer, count, VertTag, 1u);
            count = AppendFeature(buffer, count, Vrt2Tag, 1u);
            count = AppendFeature(buffer, count, VknaTag, 1u);
            count = AppendFeature(buffer, count, VkrnTag, applyKerning ? 1u : 0u);
        }

        if (count == 0)
        {
            return Array.Empty<HarfBuzzSharp.Feature>();
        }

        var result = new HarfBuzzSharp.Feature[count];
        buffer[..count].CopyTo(result);
        return result;
    }

    private static bool HasVerticalLayout(TextStyle style)
    {
        return DocTextDirectionHelpers.IsVertical(style.TextDirection);
    }

    internal static float ResolveVerticalAdvanceScale(TextStyle style)
    {
        if (!HasVerticalLayout(style))
        {
            return 1f;
        }

        return style.EastAsianLayout?.VerticalCompress == true
            ? VerticalCompressionScale
            : 1f;
    }

    private static int AppendFeature(Span<HarfBuzzSharp.Feature> features, int count, HarfBuzzSharp.Tag tag, uint value)
    {
        if ((uint)count >= (uint)features.Length)
        {
            return count;
        }

        features[count++] = new HarfBuzzSharp.Feature(tag, value);
        return count;
    }

    private static HarfBuzzSharp.Tag CreateStylisticTag(int setIndex)
    {
        var tens = (char)('0' + (setIndex / 10));
        var ones = (char)('0' + (setIndex % 10));
        return new HarfBuzzSharp.Tag('s', 's', tens, ones);
    }

    internal static TextShapeInfo BuildSimpleShapeInfo(ReadOnlySpan<char> text, SKPaint paint, TextStyle style)
    {
        if (text.IsEmpty)
        {
            return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
        }

        var offsets = new List<int>();
        var advances = new List<float>();
        var advanceScale = ResolveVerticalAdvanceScale(style);
        var applyAdvanceScale = advanceScale != 1f;
        var index = 0;
        while (index < text.Length)
        {
            var clusterLength = GetNextClusterLength(text, index);
            offsets.Add(index);
            var advance = paint.MeasureText(text.Slice(index, clusterLength));
            if (applyAdvanceScale)
            {
                advance *= advanceScale;
            }
            advances.Add(advance);
            index += clusterLength;
        }

        return offsets.Count == 0
            ? new TextShapeInfo(text.Length, Array.Empty<int>(), Array.Empty<float>())
            : new TextShapeInfo(text.Length, offsets.ToArray(), advances.ToArray());
    }

    internal readonly record struct HarfBuzzGlyphRun(
        ushort[] Glyphs,
        SKPoint[] Positions,
        uint[] Clusters,
        TextShapeInfo ShapeInfo,
        float Width);

    private readonly struct HarfBuzzFontContext : IDisposable
    {
        public HarfBuzzFontContext(HarfBuzzSharp.Font font, HarfBuzzSharp.Face face, HarfBuzzSharp.Blob blob)
        {
            Font = font;
            _face = face;
            _blob = blob;
        }

        public HarfBuzzSharp.Font Font { get; }

        private readonly HarfBuzzSharp.Face _face;
        private readonly HarfBuzzSharp.Blob _blob;

        public void Dispose()
        {
            Font.Dispose();
            _face.Dispose();
            _blob.Dispose();
        }
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
        var useScriptFonts = RequiresScriptSegmentation(style);
        if (fallbackResolver is null || (!useScriptFonts && basePaint.ContainsGlyphs(text)))
        {
            segments.Add(new TypefaceSegment(0, text.Length, baseTypeface));
            return segments;
        }

        var index = 0;
        var lastStrong = TextScriptKind.Latin;
        TextStyle? latinStyle = null;
        TextStyle? eastAsiaStyle = null;
        TextStyle? complexStyle = null;
        SKTypeface? latinTypeface = null;
        SKTypeface? eastAsiaTypeface = null;
        SKTypeface? complexTypeface = null;
        var resolver = fallbackResolver as ISkiaTypefaceResolver;
        while (index < text.Length)
        {
            var clusterLength = GetNextClusterLength(text, index);
            var clusterSpan = text.Slice(index, clusterLength);
            var segmentStyle = style;
            if (useScriptFonts)
            {
                var script = ClassifyClusterScript(clusterSpan, ref lastStrong);
                segmentStyle = ResolveScriptStyle(style, script, ref latinStyle, ref eastAsiaStyle, ref complexStyle);
            }

            var typeface = baseTypeface;
            if (resolver is not null && !ReferenceEquals(segmentStyle, style))
            {
                if (ReferenceEquals(segmentStyle, latinStyle))
                {
                    typeface = latinTypeface ??= resolver.ResolveTypeface(segmentStyle);
                }
                else if (ReferenceEquals(segmentStyle, eastAsiaStyle))
                {
                    typeface = eastAsiaTypeface ??= resolver.ResolveTypeface(segmentStyle);
                }
                else if (ReferenceEquals(segmentStyle, complexStyle))
                {
                    typeface = complexTypeface ??= resolver.ResolveTypeface(segmentStyle);
                }
                else
                {
                    typeface = resolver.ResolveTypeface(segmentStyle);
                }
            }

            if (!typeface.ContainsGlyphs(clusterSpan))
            {
                var fallback = fallbackResolver.ResolveFallbackTypeface(segmentStyle, clusterSpan);
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

    private static bool RequiresScriptSegmentation(TextStyle style)
    {
        return !string.IsNullOrWhiteSpace(style.FontFamilyAscii)
               || !string.IsNullOrWhiteSpace(style.FontFamilyHighAnsi)
               || !string.IsNullOrWhiteSpace(style.FontFamilyEastAsia)
               || !string.IsNullOrWhiteSpace(style.FontFamilyComplexScript)
               || style.FontSizeComplexScript.HasValue
               || !string.IsNullOrWhiteSpace(style.LanguageEastAsia)
               || !string.IsNullOrWhiteSpace(style.LanguageBidi);
    }

    private static TextScriptKind ClassifyClusterScript(ReadOnlySpan<char> cluster, ref TextScriptKind lastStrong)
    {
        var index = 0;
        while (index < cluster.Length)
        {
            if (!Utf16Decoder.TryDecodeFromUtf16(cluster.Slice(index), out var rune, out var consumed))
            {
                rune = new Rune(cluster[index]);
                consumed = 1;
            }

            var script = TextScript.ClassifyRune(rune);
            if (script != TextScriptKind.Neutral)
            {
                lastStrong = script;
                return script;
            }

            index += consumed;
        }

        return lastStrong;
    }

    private static TextStyle ResolveScriptStyle(
        TextStyle style,
        TextScriptKind script,
        ref TextStyle? latinStyle,
        ref TextStyle? eastAsiaStyle,
        ref TextStyle? complexStyle)
    {
        return script switch
        {
            TextScriptKind.EastAsian => eastAsiaStyle ??= BuildScriptStyle(style, script),
            TextScriptKind.Complex => complexStyle ??= BuildScriptStyle(style, script),
            _ => latinStyle ??= BuildScriptStyle(style, TextScriptKind.Latin)
        };
    }

    private static TextStyle BuildScriptStyle(TextStyle style, TextScriptKind script)
    {
        var family = style.FontFamily;
        var size = style.FontSize;
        var language = style.Language;

        switch (script)
        {
            case TextScriptKind.EastAsian:
                family = style.FontFamilyEastAsia ?? family;
                language = style.LanguageEastAsia ?? language;
                break;
            case TextScriptKind.Complex:
                family = style.FontFamilyComplexScript ?? family;
                if (style.FontSizeComplexScript.HasValue && style.FontSizeComplexScript.Value > 0f)
                {
                    size = style.FontSizeComplexScript.Value;
                }
                language = style.LanguageBidi ?? language;
                break;
            case TextScriptKind.Latin:
                family = style.FontFamilyAscii ?? style.FontFamilyHighAnsi ?? family;
                break;
        }

        if (string.Equals(family, style.FontFamily, StringComparison.Ordinal)
            && size.Equals(style.FontSize)
            && string.Equals(language, style.Language, StringComparison.Ordinal))
        {
            return style;
        }

        var resolved = style.Clone();
        resolved.FontFamily = family;
        resolved.FontSize = size;
        resolved.Language = language;
        return resolved;
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

    internal static TextShapeInfo BuildShapeInfo(int textLength, SKShaper.Result result, float advanceScale)
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
            var lastPoint = points[glyphCount - 1].X;
            totalWidth = float.IsNaN(lastPoint) || float.IsInfinity(lastPoint) ? 0f : MathF.Abs(lastPoint);
        }
        var glyphAdvances = new float[glyphCount];
        for (var i = 0; i < glyphCount; i++)
        {
            var current = points[i].X;
            var next = i + 1 < glyphCount ? points[i + 1].X : totalWidth;
            if (float.IsNaN(current) || float.IsNaN(next) || float.IsInfinity(current) || float.IsInfinity(next))
            {
                return new TextShapeInfo(textLength, Array.Empty<int>(), Array.Empty<float>());
            }

            var advance = next - current;
            if (advanceScale != 1f)
            {
                advance *= advanceScale;
            }
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
        buffer.GuessSegmentProperties();
        if (!string.IsNullOrWhiteSpace(style.Language))
        {
            buffer.Language = new HarfBuzzSharp.Language(style.Language);
        }
        if (TextScript.TryGetScriptTag(text, out var scriptTag))
        {
            buffer.Script = (HarfBuzzSharp.Script)scriptTag;
        }
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

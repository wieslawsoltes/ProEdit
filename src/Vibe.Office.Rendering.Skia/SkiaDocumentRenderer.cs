using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;

namespace Vibe.Office.Rendering.Skia;

public sealed partial class SkiaDocumentRenderer : IDocumentRenderer<SKCanvas>
{
    private static readonly string[] AsciiCharCache = BuildAsciiCharCache();
    private DocumentLayout? _cachedLayout;
    private readonly Dictionary<int, SKPicture> _pageCache = new();
    private long _lastDirtyVersion = -1;
    public ISkiaTypefaceResolver? TypefaceResolver { get; set; }

    public void Render(SKCanvas canvas, Document document, DocumentLayout layout, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);

        canvas.Clear(ToSkColor(options.BackgroundColor));

        var style = document.DefaultTextStyle;
        var styleResolver = new DocumentStyleResolver(document);
        var paintCache = new Dictionary<TextStyleKey, SKPaint>();
        var highlightPaintCache = new Dictionary<DocColor, SKPaint>();
        var fillPaintCache = new Dictionary<DocColor, SKPaint>();
        var borderPaintCache = new Dictionary<BorderPaintKey, SKPaint>();
        var invisibleTextPaintCache = new Dictionary<TextStyleKey, SKPaint>();
        var shaperCache = new Dictionary<TextStyleKey, SKShaper>();
        var typefacePaintCache = new Dictionary<(TextStyleKey StyleKey, SKTypeface Typeface), SKPaint>();
        var typefaceShaperCache = new Dictionary<SKTypeface, SKShaper>();
        var canShapeText = options.UseHarfBuzz;
        var fallbackResolver = TypefaceResolver as ISkiaTypefaceFallbackResolver;

        SKPaint GetRunPaint(TextStyle runStyle)
        {
            var key = new TextStyleKey(runStyle);
            if (paintCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var paint = SkiaTextMeasurer.CreatePaint(runStyle, TypefaceResolver);
            paint.Color = ToSkColor(runStyle.Color);
            paintCache[key] = paint;
            return paint;
        }

        SKPaint GetTypefacePaint(TextStyle runStyle, SKTypeface typeface)
        {
            var basePaint = GetRunPaint(runStyle);
            if (ReferenceEquals(basePaint.Typeface, typeface))
            {
                return basePaint;
            }

            var key = (new TextStyleKey(runStyle), typeface);
            if (typefacePaintCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var paint = SkiaTextMeasurer.CreatePaint(runStyle, null);
            paint.Typeface = typeface;
            paint.Color = ToSkColor(runStyle.Color);
            typefacePaintCache[key] = paint;
            return paint;
        }

        void DisableShaping()
        {
            canShapeText = false;
            foreach (var shaper in shaperCache.Values)
            {
                shaper.Dispose();
            }

            shaperCache.Clear();

            foreach (var shaper in typefaceShaperCache.Values)
            {
                shaper.Dispose();
            }

            typefaceShaperCache.Clear();
        }

        SKShaper? GetRunShaper(TextStyle runStyle)
        {
            if (!canShapeText)
            {
                return null;
            }

            var key = new TextStyleKey(runStyle);
            if (shaperCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            try
            {
                var paint = GetRunPaint(runStyle);
                var typeface = paint.Typeface ?? SKTypeface.Default;
                var shaper = new SKShaper(typeface);
                shaperCache[key] = shaper;
                return shaper;
            }
            catch
            {
                DisableShaping();
                return null;
            }
        }

        SKShaper? GetTypefaceShaper(SKTypeface typeface)
        {
            if (!canShapeText)
            {
                return null;
            }

            if (typefaceShaperCache.TryGetValue(typeface, out var cached))
            {
                return cached;
            }

            try
            {
                var shaper = new SKShaper(typeface);
                typefaceShaperCache[typeface] = shaper;
                return shaper;
            }
            catch
            {
                DisableShaping();
                return null;
            }
        }

        var runMetricsCache = new Dictionary<RunMetricsKey, RunMetrics>();

        int?[] BuildLineNumbers()
        {
            if (layout.Lines.Count == 0)
            {
                return Array.Empty<int?>();
            }

            var result = new int?[layout.Lines.Count];
            var paragraphCount = document.ParagraphCount;
            var suppress = new bool[paragraphCount];
            for (var i = 0; i < paragraphCount; i++)
            {
                var paragraph = document.GetParagraph(i);
                var properties = styleResolver.ResolveParagraphProperties(paragraph);
                suppress[i] = properties.SuppressLineNumbers == true || properties.Frame?.HasValues == true;
            }

            var currentNumber = 1;
            var currentStart = 1;
            var currentSectionIndex = -1;
            var currentPageIndex = -1;
            var numberingEnabled = false;
            LineNumberingSettings? currentSettings = null;

            for (var i = 0; i < layout.Lines.Count; i++)
            {
                var line = layout.Lines[i];
                if (line.ParagraphIndex < 0)
                {
                    continue;
                }

                if (!layout.ParagraphSectionIndices.TryGetValue(line.ParagraphIndex, out var sectionIndex))
                {
                    sectionIndex = 0;
                }

                var pageIndex = layout.LineIndex.GetPageForLine(i);
                var sectionChanged = sectionIndex != currentSectionIndex;
                if (sectionChanged)
                {
                    var hadNumbering = numberingEnabled;
                    currentSectionIndex = sectionIndex;
                    currentSettings = layout.SectionSettings.TryGetValue(sectionIndex, out var section)
                        ? section.LineNumbering
                        : null;
                    numberingEnabled = currentSettings is not null;
                    if (numberingEnabled)
                    {
                        var settings = currentSettings!;
                        if (!hadNumbering || settings.Restart == LineNumberRestart.NewSection)
                        {
                            currentNumber = Math.Max(1, settings.Start ?? 1);
                            currentStart = currentNumber;
                        }
                        else if (settings.Start.HasValue)
                        {
                            currentNumber = Math.Max(1, settings.Start.Value);
                            currentStart = currentNumber;
                        }
                    }
                }

                if (!numberingEnabled || currentSettings is null)
                {
                    continue;
                }

                if (currentSettings.Restart == LineNumberRestart.NewPage && pageIndex != currentPageIndex)
                {
                    currentNumber = Math.Max(1, currentSettings.Start ?? 1);
                    currentStart = currentNumber;
                }

                currentPageIndex = pageIndex;

                if (line.IsInTable)
                {
                    continue;
                }

                if (line.ParagraphIndex < suppress.Length && suppress[line.ParagraphIndex])
                {
                    continue;
                }

                var countBy = currentSettings.CountBy ?? 1;
                if (countBy <= 0)
                {
                    countBy = 1;
                }

                if ((currentNumber - currentStart) % countBy == 0)
                {
                    result[i] = currentNumber;
                }

                currentNumber++;
            }

            return result;
        }

        var lineNumberStyle = style.Clone();
        lineNumberStyle.FontSize = MathF.Max(8f, style.FontSize * 0.8f);
        lineNumberStyle.Color = options.TextColor;
        var lineNumberPaint = GetRunPaint(lineNumberStyle);
        var lineNumbers = BuildLineNumbers();

        TextShapeInfo ShapeText(string text, TextStyle runStyle)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
            }

            void AppendShapeInfo(TextShapeInfo segmentShape, int offsetBase, List<int> offsets, List<float> advances)
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

            var paint = GetRunPaint(runStyle);
            var textSpan = text.AsSpan();
            var needsFallback = fallbackResolver is not null && !paint.ContainsGlyphs(text);
            if (!needsFallback)
            {
                var shaper = GetRunShaper(runStyle);
                if (shaper is not null)
                {
                    try
                    {
                        if (SkiaTextMeasurer.TryShapeTextSegment(textSpan, runStyle, paint, shaper, out var shaped))
                        {
                            return shaped;
                        }
                    }
                    catch
                    {
                        // fall back to cluster measurement
                    }
                }

                return SkiaTextMeasurer.BuildSimpleShapeInfo(textSpan, paint);
            }

            var segments = SkiaTextMeasurer.BuildTypefaceSegments(textSpan, runStyle, paint, fallbackResolver);
            if (segments.Count == 0)
            {
                return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
            }

            var segmentOffsets = new List<int>();
            var segmentAdvances = new List<float>();
            foreach (var segment in segments)
            {
                var segmentSpan = textSpan.Slice(segment.Start, segment.Length);
                var segmentPaint = GetTypefacePaint(runStyle, segment.Typeface);
                if (canShapeText)
                {
                    var shaper = GetTypefaceShaper(segment.Typeface);
                    if (shaper is not null)
                    {
                        try
                        {
                            if (SkiaTextMeasurer.TryShapeTextSegment(segmentSpan, runStyle, segmentPaint, shaper, out var segmentShape))
                            {
                                AppendShapeInfo(segmentShape, segment.Start, segmentOffsets, segmentAdvances);
                                continue;
                            }
                        }
                        catch
                        {
                            // fall back to cluster measurement
                        }
                    }
                }

                var fallbackShape = SkiaTextMeasurer.BuildSimpleShapeInfo(segmentSpan, segmentPaint);
                AppendShapeInfo(fallbackShape, segment.Start, segmentOffsets, segmentAdvances);
            }

            return segmentOffsets.Count == 0
                ? new TextShapeInfo(text.Length, Array.Empty<int>(), Array.Empty<float>())
                : new TextShapeInfo(text.Length, segmentOffsets.ToArray(), segmentAdvances.ToArray());
        }

        RunMetrics GetRunMetrics(string text, TextStyle runStyle, float letterSpacing, float gridSpacing)
        {
            if (string.IsNullOrEmpty(text))
            {
                return RunMetrics.Empty;
            }

            var key = new RunMetricsKey(text, runStyle, letterSpacing, gridSpacing);
            if (runMetricsCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var shape = ShapeText(text, runStyle);
            var metrics = new RunMetrics(shape, letterSpacing, gridSpacing);
            runMetricsCache[key] = metrics;
            return metrics;
        }

        float ResolveGridSpacing(DocGridSettings? docGrid)
        {
            if (docGrid is null || !docGrid.HasValues)
            {
                return 0f;
            }

            if (docGrid.CharacterSpace is not > 0f)
            {
                return 0f;
            }

            return !docGrid.Type.HasValue
                || docGrid.Type == DocGridType.LinesAndChars
                || docGrid.Type == DocGridType.SnapToChars
                ? docGrid.CharacterSpace.Value
                : 0f;
        }

        float ResolveLineGridSpacing(int paragraphIndex, int pageIndex)
        {
            if (paragraphIndex >= 0
                && layout.ParagraphSectionIndices.TryGetValue(paragraphIndex, out var sectionIndex)
                && layout.SectionSettings.TryGetValue(sectionIndex, out var section))
            {
                return ResolveGridSpacing(section.ResolveForPage(pageIndex).DocGrid);
            }

            return pageIndex >= 0 && pageIndex < layout.PageSections.Count
                ? ResolveGridSpacing(layout.PageSections[pageIndex].DocGrid)
                : 0f;
        }

        SKPaint GetHighlightPaint(DocColor color)
        {
            if (highlightPaintCache.TryGetValue(color, out var cached))
            {
                return cached;
            }

            var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ToSkColor(color),
                IsAntialias = true
            };
            highlightPaintCache[color] = paint;
            return paint;
        }

        SKPaint GetFillPaint(DocColor color)
        {
            if (fillPaintCache.TryGetValue(color, out var cached))
            {
                return cached;
            }

            var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ToSkColor(color),
                IsAntialias = true
            };
            fillPaintCache[color] = paint;
            return paint;
        }

        SKPaint GetInvisibleTextPaint(TextStyle runStyle)
        {
            var key = new TextStyleKey(runStyle);
            if (invisibleTextPaintCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var paint = SkiaTextMeasurer.CreatePaint(runStyle, TypefaceResolver);
            paint.Color = ToSkColor(options.InvisiblesColor);
            invisibleTextPaintCache[key] = paint;
            return paint;
        }

        SKPaint GetBorderPaint(BorderLine border, float thickness)
        {
            var key = new BorderPaintKey(border.Color, thickness, border.Style);
            if (borderPaintCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = ToSkColor(border.Color),
                StrokeWidth = thickness,
                IsAntialias = true,
                StrokeCap = border.Style == DocBorderStyle.Dotted ? SKStrokeCap.Round : SKStrokeCap.Butt,
                PathEffect = CreateBorderEffect(border.Style, thickness)
            };
            borderPaintCache[key] = paint;
            return paint;
        }

        using var defaultPaint = SkiaTextMeasurer.CreatePaint(style, TypefaceResolver);
        defaultPaint.Color = ToSkColor(options.TextColor);

        using var pageBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.PageBorderColor),
            StrokeWidth = options.PageBorderThickness,
            IsAntialias = true
        };

        using var columnSeparatorPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.ColumnSeparatorColor),
            StrokeWidth = options.ColumnSeparatorThickness,
            IsAntialias = true
        };

        using var selectionPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.SelectionColor),
            IsAntialias = true
        };

        using var floatingSelectionPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.FloatingSelectionColor),
            StrokeWidth = 1.5f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0f)
        };

        using var caretPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.CaretColor),
            IsAntialias = true
        };

        using var invisiblesStrokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.InvisiblesColor),
            StrokeWidth = 1f,
            IsAntialias = true
        };

        using var invisiblesFillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.InvisiblesColor),
            IsAntialias = true
        };

        var layoutGuideColor = options.LayoutGuideColor;
        var layoutGuideLightAlpha = (byte)Math.Clamp((int)(layoutGuideColor.A * 0.5f), 40, 180);
        var layoutGuideLightColor = new DocColor(layoutGuideColor.R, layoutGuideColor.G, layoutGuideColor.B, layoutGuideLightAlpha);

        using var layoutGuidePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(layoutGuideColor),
            StrokeWidth = options.LayoutGuideThickness,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 5f, 3f }, 0f)
        };

        using var layoutGuideLightPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(layoutGuideLightColor),
            StrokeWidth = MathF.Max(0.75f, options.LayoutGuideThickness * 0.75f),
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0f)
        };

        var selection = options.Selection?.Normalize();
        var commentHighlightsByParagraph = layout.CommentHighlightsByParagraph;
        var footnoteMap = layout.Footnotes.ToDictionary(footnote => footnote.PageIndex);

        using var footnoteSeparatorPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToSkColor(options.PageBorderColor),
            StrokeWidth = 1f,
            IsAntialias = true
        };

        var targetCanvas = canvas;

        void DrawLineHighlights(
            float lineX,
            float lineY,
            float lineHeight,
            DocTextDirection? textDirection,
            ReadOnlySpan<char> lineText,
            bool baseRtl,
            IReadOnlyList<LayoutRun> runs,
            IReadOnlyList<LayoutImage> images,
            IReadOnlyList<LayoutShape> shapes,
            IReadOnlyList<LayoutChart> charts,
            IReadOnlyList<LayoutEquation> equations,
            float gridSpacing)
        {
            var segments = BuildVisualSegments(lineText, baseRtl, runs, images, shapes, charts, equations, gridSpacing, GetRunMetrics);
            foreach (var segment in segments)
            {
                if (!segment.IsText || segment.Run is null)
                {
                    continue;
                }

                var run = segment.Run;
                if (run.Style.HighlightColor is null || string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                var highlightPaint = GetHighlightPaint(run.Style.HighlightColor.Value);
                DrawLineRangeRect(lineX, lineY, lineHeight, textDirection, segment.X, segment.X + segment.Width, highlightPaint);
            }
        }

        void DrawCommentHighlights(
            int paragraphIndex,
            int lineStart,
            int lineLength,
            float lineX,
            float lineY,
            float lineHeight,
            DocTextDirection? textDirection,
            ReadOnlySpan<char> lineText,
            bool baseRtl,
            IReadOnlyList<LayoutRun> runs,
            IReadOnlyList<LayoutImage> images,
            IReadOnlyList<LayoutShape> shapes,
            IReadOnlyList<LayoutChart> charts,
            IReadOnlyList<LayoutEquation> equations,
            float gridSpacing)
        {
            if (lineLength <= 0 || commentHighlightsByParagraph.Count == 0 || options.CommentHighlightColor.A == 0)
            {
                return;
            }

            if (!commentHighlightsByParagraph.TryGetValue(paragraphIndex, out var spans))
            {
                return;
            }

            var highlightPaint = GetHighlightPaint(options.CommentHighlightColor);
            var lineEnd = lineStart + lineLength;
            foreach (var span in spans)
            {
                var spanStart = Math.Max(lineStart, span.StartOffset);
                var spanEnd = Math.Min(lineEnd, span.EndOffset);
                if (spanEnd <= spanStart)
                {
                    continue;
                }

                var startOffset = spanStart - lineStart;
                var endOffset = spanEnd - lineStart;
                var highlightX1 = MeasureLineOffset(lineText, baseRtl, runs, images, shapes, charts, equations, startOffset, gridSpacing, GetRunMetrics);
                var highlightX2 = MeasureLineOffset(lineText, baseRtl, runs, images, shapes, charts, equations, endOffset, gridSpacing, GetRunMetrics);
                if (highlightX2 <= highlightX1)
                {
                    continue;
                }

                DrawLineRangeRect(lineX, lineY, lineHeight, textDirection, highlightX1, highlightX2, highlightPaint);
            }
        }

        void DrawLineRangeRect(
            float lineX,
            float lineY,
            float lineHeight,
            DocTextDirection? textDirection,
            float start,
            float end,
            SKPaint paint)
        {
            if (end <= start)
            {
                return;
            }

            if (!DocTextDirectionHelpers.IsVertical(textDirection))
            {
                var rect = new SKRect(lineX + start, lineY, lineX + end, lineY + lineHeight);
                targetCanvas.DrawRect(rect, paint);
                return;
            }

            targetCanvas.Save();
            targetCanvas.Translate(lineX, lineY);
            targetCanvas.RotateDegrees(DocTextDirectionHelpers.GetRotationDegrees(textDirection!.Value));
            var localRect = new SKRect(start, 0f, end, lineHeight);
            targetCanvas.DrawRect(localRect, paint);
            targetCanvas.Restore();
        }

        void DrawLineContent(
            float lineX,
            float lineY,
            float lineHeight,
            float lineAscent,
            string? prefix,
            float prefixWidth,
            ReadOnlySpan<char> lineText,
            bool baseRtl,
            IReadOnlyList<LayoutRun> runs,
            IReadOnlyList<LayoutImage> images,
            IReadOnlyList<LayoutShape> shapes,
            IReadOnlyList<LayoutChart> charts,
            IReadOnlyList<LayoutEquation> equations,
            IReadOnlyList<LayoutRuby> rubies,
            DocTextDirection? textDirection,
            float gridSpacing)
        {
            var originX = lineX;
            var originY = lineY;
            var restoreTransform = false;
            if (DocTextDirectionHelpers.IsVertical(textDirection))
            {
                restoreTransform = true;
                targetCanvas.Save();
                targetCanvas.Translate(lineX, lineY);
                var rotation = DocTextDirectionHelpers.GetRotationDegrees(textDirection!.Value);
                targetCanvas.RotateDegrees(rotation);
                originX = 0f;
                originY = 0f;
            }

            var baseline = originY + lineAscent;
            var segments = BuildVisualSegments(lineText, baseRtl, runs, images, shapes, charts, equations, gridSpacing, GetRunMetrics);
            if (!string.IsNullOrEmpty(prefix))
            {
                var lineWidth = segments.Count > 0 ? segments[^1].X + segments[^1].Width : 0f;
                var prefixX = baseRtl ? originX + lineWidth : originX - prefixWidth;
                var prefixBaseline = originY + lineAscent;
                var prefixShaper = GetRunShaper(style);
                if (prefixShaper is null)
                {
                    targetCanvas.DrawText(prefix, prefixX, prefixBaseline, defaultPaint);
                }
                else
                {
                    targetCanvas.DrawShapedText(prefixShaper, prefix, prefixX, prefixBaseline, defaultPaint);
                }
            }
            foreach (var segment in segments)
            {
                if (segment.IsTab && segment.Run is not null)
                {
                    var run = segment.Run;
                    if (run.TabLeader != TabLeader.None && segment.Width > 0f)
                    {
                        var leaderChar = run.TabLeader switch
                        {
                            TabLeader.Dot => '.',
                            TabLeader.Hyphen => '-',
                            TabLeader.Underscore => '_',
                            _ => '\0'
                        };

                        if (leaderChar != '\0')
                        {
                            var paint = GetRunPaint(run.Style);
                            var glyphWidth = MeasureChar(paint, leaderChar);
                            if (glyphWidth > 0f)
                            {
                                var count = Math.Max(1, (int)MathF.Ceiling(segment.Width / glyphWidth));
                                var text = new string(leaderChar, count);
                                var startX = originX + segment.X;
                                var clipRect = new SKRect(startX, originY, startX + segment.Width, originY + lineHeight);
                                targetCanvas.Save();
                                targetCanvas.ClipRect(clipRect);
                                targetCanvas.DrawText(text, startX, baseline, paint);
                                targetCanvas.Restore();
                            }
                        }
                    }

                    continue;
                }

                if (segment.IsText && segment.Run is not null)
                {
                    var run = segment.Run;
                    if (string.IsNullOrEmpty(run.Text))
                    {
                        continue;
                    }

                    var runBaseline = baseline - run.BaselineOffset;
                    var runPaint = GetRunPaint(run.Style);
                    var segmentText = run.Text.Substring(segment.RunStart, segment.Length);
                    var segmentSpan = segmentText.AsSpan();
                    var segmentX = originX + segment.X;
                    var drawX = segment.IsRtl ? segmentX + segment.Width : segmentX;
                    var baseTypeface = runPaint.Typeface ?? SKTypeface.Default;
                    var fallbackSegments = fallbackResolver is not null && !runPaint.ContainsGlyphs(segmentText)
                        ? SkiaTextMeasurer.BuildTypefaceSegments(segmentSpan, run.Style, runPaint, fallbackResolver)
                        : null;

                    if (fallbackSegments is null
                        || fallbackSegments.Count == 0
                        || (fallbackSegments.Count == 1 && ReferenceEquals(fallbackSegments[0].Typeface, baseTypeface)))
                    {
                        var shaper = GetRunShaper(run.Style);
                        DrawTextWithSpacing(targetCanvas, segmentText, drawX, runBaseline, runPaint, shaper, run.LetterSpacing, gridSpacing, run.Style);
                    }
                    else
                    {
                        var segmentMetrics = GetRunMetrics(segmentText, run.Style, run.LetterSpacing, gridSpacing);
                        var metricsWidth = segmentMetrics.Width;
                        var scale = metricsWidth > 0f ? segment.Width / metricsWidth : 1f;

                        foreach (var fallbackSegment in fallbackSegments)
                        {
                            var fallbackText = segmentSpan.Slice(fallbackSegment.Start, fallbackSegment.Length).ToString();
                            var fallbackPaint = GetTypefacePaint(run.Style, fallbackSegment.Typeface);
                            var fallbackShaper = GetTypefaceShaper(fallbackSegment.Typeface);
                            var localX = segmentMetrics.GetWidth(fallbackSegment.Start) * scale;
                            var fallbackX = segment.IsRtl ? drawX - localX : drawX + localX;

                            DrawTextWithSpacing(targetCanvas, fallbackText, fallbackX, runBaseline, fallbackPaint, fallbackShaper, run.LetterSpacing, gridSpacing, run.Style);
                        }
                    }

                    DrawUnderlineSpan(targetCanvas, runBaseline, segmentX, segment.Width, segmentText, run.Style, run.LetterSpacing, runPaint);
                    DrawStrikeThroughSpan(targetCanvas, runBaseline, segmentX, segment.Width, segmentText, run.Style, runPaint);
                    continue;
                }

                if (segment.Image is not null)
                {
                    DrawImage(targetCanvas, segment.Image with { X = segment.X }, originX, baseline, lineAscent, options);
                }
                else if (segment.Shape is not null)
                {
                    DrawShape(targetCanvas, segment.Shape with { X = segment.X }, originX, baseline, lineAscent, options, style);
                }
                else if (segment.Chart is not null)
                {
                    DrawChart(targetCanvas, segment.Chart with { X = segment.X }, originX, baseline, options);
                }
                else if (segment.Equation is not null)
                {
                    DrawEquation(targetCanvas, segment.Equation with { X = segment.X }, originX, baseline, GetRunPaint, GetRunShaper);
                }
            }

            if (rubies.Count > 0)
            {
                foreach (var ruby in rubies)
                {
                    if (ruby.Length <= 0 || string.IsNullOrEmpty(ruby.RubyText))
                    {
                        continue;
                    }

                    var startX = MeasureLineOffset(lineText, baseRtl, runs, images, shapes, charts, equations, ruby.StartOffset, gridSpacing, GetRunMetrics);
                    var endX = MeasureLineOffset(lineText, baseRtl, runs, images, shapes, charts, equations, ruby.StartOffset + ruby.Length, gridSpacing, GetRunMetrics);
                    var rangeX = MathF.Min(startX, endX);
                    var baseWidth = MathF.Abs(endX - startX);

                    var rubyMetrics = GetRunMetrics(ruby.RubyText, ruby.RubyStyle, 0f, gridSpacing);
                    var rubyWidth = rubyMetrics.Width;
                    var rubyX = originX + rangeX + MathF.Max(0f, (baseWidth - rubyWidth) / 2f);
                    var rubyBaseline = baseline + ruby.BaselineOffset;

                    var rubyPaint = GetRunPaint(ruby.RubyStyle);
                    var rubyShaper = GetRunShaper(ruby.RubyStyle);
                    var rubySpan = ruby.RubyText.AsSpan();
                    var baseTypeface = rubyPaint.Typeface ?? SKTypeface.Default;
                    var fallbackSegments = fallbackResolver is not null && !rubyPaint.ContainsGlyphs(ruby.RubyText)
                        ? SkiaTextMeasurer.BuildTypefaceSegments(rubySpan, ruby.RubyStyle, rubyPaint, fallbackResolver)
                        : null;

                    if (fallbackSegments is null
                        || fallbackSegments.Count == 0
                        || (fallbackSegments.Count == 1 && ReferenceEquals(fallbackSegments[0].Typeface, baseTypeface)))
                    {
                        DrawTextWithSpacing(targetCanvas, ruby.RubyText, rubyX, rubyBaseline, rubyPaint, rubyShaper, 0f, gridSpacing, ruby.RubyStyle);
                    }
                    else
                    {
                        foreach (var fallbackSegment in fallbackSegments)
                        {
                            var fallbackText = rubySpan.Slice(fallbackSegment.Start, fallbackSegment.Length).ToString();
                            var fallbackPaint = GetTypefacePaint(ruby.RubyStyle, fallbackSegment.Typeface);
                            var fallbackShaper = GetTypefaceShaper(fallbackSegment.Typeface);
                            var localX = rubyMetrics.GetWidth(fallbackSegment.Start);
                            var fallbackX = rubyX + localX;

                            DrawTextWithSpacing(targetCanvas, fallbackText, fallbackX, rubyBaseline, fallbackPaint, fallbackShaper, 0f, gridSpacing, ruby.RubyStyle);
                        }
                    }
                }
            }

            if (restoreTransform)
            {
                targetCanvas.Restore();
            }
        }

        void DrawLineInvisibles(
            float lineX,
            float lineY,
            float lineHeight,
            float lineAscent,
            ReadOnlySpan<char> lineText,
            bool baseRtl,
            IReadOnlyList<LayoutRun> runs,
            IReadOnlyList<LayoutImage> images,
            IReadOnlyList<LayoutShape> shapes,
            IReadOnlyList<LayoutChart> charts,
            IReadOnlyList<LayoutEquation> equations,
            DocTextDirection? textDirection,
            float gridSpacing,
            bool showParagraphMark,
            float paragraphMarkOffset)
        {
            if (!options.ShowInvisibles)
            {
                return;
            }

            var originX = lineX;
            var originY = lineY;
            var restoreTransform = false;
            if (DocTextDirectionHelpers.IsVertical(textDirection))
            {
                restoreTransform = true;
                targetCanvas.Save();
                targetCanvas.Translate(lineX, lineY);
                targetCanvas.RotateDegrees(DocTextDirectionHelpers.GetRotationDegrees(textDirection!.Value));
                originX = 0f;
                originY = 0f;
            }

            var baseline = originY + lineAscent;
            var dotY = baseline - lineAscent * 0.2f;
            var arrowSize = MathF.Max(4f, lineAscent * 0.3f);

            var segments = BuildVisualSegments(lineText, baseRtl, runs, images, shapes, charts, equations, gridSpacing, GetRunMetrics);

            float MeasureOffsetFromSegments(int length)
            {
                if (length <= 0 || segments.Count == 0)
                {
                    return 0f;
                }

                var totalWidth = segments[^1].X + segments[^1].Width;
                var target = Math.Clamp(length, 0, int.MaxValue);
                VisualSegment? containing = null;
                foreach (var segment in segments)
                {
                    if (target == segment.StartOffset)
                    {
                        containing = segment;
                        break;
                    }

                    if (containing is null && target > segment.StartOffset && target <= segment.StartOffset + segment.Length)
                    {
                        containing = segment;
                    }
                }

                if (containing is not null)
                {
                    var offsetInSegment = Math.Clamp(target - containing.StartOffset, 0, containing.Length);
                    var localX = MeasureSegmentOffset(containing, offsetInSegment, gridSpacing, GetRunMetrics);
                    return containing.X + localX;
                }

                return target <= 0 ? 0f : totalWidth;
            }

            foreach (var segment in segments)
            {
                if (!segment.IsTab)
                {
                    continue;
                }

                var startX = originX + segment.X;
                var endX = startX + segment.Width;
                if (segment.IsRtl)
                {
                    (startX, endX) = (endX, startX);
                }

                var lineEnd = segment.IsRtl
                    ? MathF.Min(startX, endX + arrowSize)
                    : MathF.Max(startX, endX - arrowSize);
                targetCanvas.DrawLine(startX, baseline, lineEnd, baseline, invisiblesStrokePaint);
                targetCanvas.DrawLine(lineEnd, baseline, lineEnd - arrowSize * 0.6f, baseline - arrowSize * 0.4f, invisiblesStrokePaint);
                targetCanvas.DrawLine(lineEnd, baseline, lineEnd - arrowSize * 0.6f, baseline + arrowSize * 0.4f, invisiblesStrokePaint);
            }

            for (var i = 0; i < lineText.Length; i++)
            {
                var ch = lineText[i];
                if (ch != ' ' && ch != '\u00A0')
                {
                    continue;
                }

                var startX = originX + MeasureOffsetFromSegments(i);
                var endX = originX + MeasureOffsetFromSegments(i + 1);
                var dotX = (startX + endX) / 2f;
                targetCanvas.DrawCircle(dotX, dotY, 1.3f, invisiblesFillPaint);
            }

            if (showParagraphMark)
            {
                var markStyle = runs.LastOrDefault(run => !run.IsTab && !string.IsNullOrEmpty(run.Text))?.Style ?? style;
                var markPaint = GetInvisibleTextPaint(markStyle);
                targetCanvas.DrawText("¶", originX + paragraphMarkOffset + 2f, baseline, markPaint);
            }

            if (restoreTransform)
            {
                targetCanvas.Restore();
            }
        }

        void DrawFloatingObject(FloatingLayoutObject floating)
        {
            var bounds = floating.Bounds;
            switch (floating.Object.Content)
            {
                case ImageInline image:
                {
                    var layoutImage = new LayoutImage(image, 0f, bounds.Width, bounds.Height, 1);
                    DrawImage(targetCanvas, layoutImage, bounds.X, bounds.Y + bounds.Height, 0f, options);
                    break;
                }
                case ShapeInline shape:
                {
                    var layoutShape = new LayoutShape(shape, 0f, bounds.Width, bounds.Height, 1);
                    DrawShape(targetCanvas, layoutShape, bounds.X, bounds.Y + bounds.Height, 0f, options, style);
                    break;
                }
                case ChartInline chart:
                {
                    var layoutChart = new LayoutChart(chart, 0f, bounds.Width, bounds.Height, 1);
                    DrawChart(targetCanvas, layoutChart, bounds.X, bounds.Y + bounds.Height, options);
                    break;
                }
            }
        }

        void DrawFloatingObjects(int pageIndex, bool behindText)
        {
            if (layout.FloatingObjects.Count == 0)
            {
                return;
            }

            foreach (var floating in layout.FloatingObjects)
            {
                if (floating.PageIndex != pageIndex)
                {
                    continue;
                }

                if (floating.Object.Anchor.BehindText != behindText)
                {
                    continue;
                }

                DrawFloatingObject(floating);
            }
        }

        var headerFooterMap = layout.HeaderFooters.ToDictionary(item => item.PageIndex);

        void DrawHeaderFooterFloatingObjects(int pageIndex, bool behindText)
        {
            if (!headerFooterMap.TryGetValue(pageIndex, out var headerFooter))
            {
                return;
            }

            if (headerFooter.FloatingObjects.Count == 0)
            {
                return;
            }

            foreach (var floating in headerFooter.FloatingObjects)
            {
                if (floating.Object.Anchor.BehindText != behindText)
                {
                    continue;
                }

                DrawFloatingObject(floating);
            }
        }

        void DrawFloatingSelection(int pageIndex)
        {
            if (!options.SelectedFloatingObjectId.HasValue || layout.FloatingObjects.Count == 0)
            {
                return;
            }

            foreach (var floating in layout.FloatingObjects)
            {
                if (floating.PageIndex != pageIndex)
                {
                    continue;
                }

                if (floating.Object.Id != options.SelectedFloatingObjectId.Value)
                {
                    continue;
                }

                var bounds = floating.Bounds;
                var selectionRect = new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
                targetCanvas.DrawRect(selectionRect, floatingSelectionPaint);
                break;
            }
        }

        static bool IntersectsPage(DocRect pageBounds, DocRect elementBounds)
        {
            return elementBounds.Bottom > pageBounds.Y && elementBounds.Y < pageBounds.Bottom;
        }

        static bool ShouldDrawPageBorder(PageBorders borders, bool isFirstPageOfSection)
        {
            return borders.Display switch
            {
                PageBorderDisplay.FirstPage => isFirstPageOfSection,
                PageBorderDisplay.ExceptFirstPage => !isFirstPageOfSection,
                _ => true
            };
        }

        void DrawPageBorders(PageLayout page, PageSectionSettings section, PageBorders borders)
        {
            var offsetFromText = borders.OffsetFrom == PageBorderOffset.Text;
            var baseBounds = offsetFromText ? page.ContentBounds : page.Bounds;
            var leftSpace = borders.Left?.Spacing ?? 0f;
            var rightSpace = borders.Right?.Spacing ?? 0f;
            var topSpace = borders.Top?.Spacing ?? 0f;
            var bottomSpace = borders.Bottom?.Spacing ?? 0f;
            var left = offsetFromText ? baseBounds.Left - leftSpace : baseBounds.Left + leftSpace;
            var right = offsetFromText ? baseBounds.Right + rightSpace : baseBounds.Right - rightSpace;
            var top = offsetFromText ? baseBounds.Top - topSpace : baseBounds.Top + topSpace;
            var bottom = offsetFromText ? baseBounds.Bottom + bottomSpace : baseBounds.Bottom - bottomSpace;

            if (right <= left || bottom <= top)
            {
                return;
            }

            if (borders.Top is { IsVisible: true } topBorder)
            {
                DrawBorderSegment(targetCanvas, topBorder, left, top, right, top, GetBorderPaint);
            }

            if (borders.Bottom is { IsVisible: true } bottomBorder)
            {
                DrawBorderSegment(targetCanvas, bottomBorder, left, bottom, right, bottom, GetBorderPaint);
            }

            if (borders.Left is { IsVisible: true } leftBorder)
            {
                DrawBorderSegment(targetCanvas, leftBorder, left, top, left, bottom, GetBorderPaint);
            }

            if (borders.Right is { IsVisible: true } rightBorder)
            {
                DrawBorderSegment(targetCanvas, rightBorder, right, top, right, bottom, GetBorderPaint);
            }
        }

        void RenderPage(int pageIndex)
        {
            var page = layout.Pages[pageIndex];
            var section = pageIndex >= 0 && pageIndex < layout.PageSections.Count
                ? layout.PageSections[pageIndex]
                : null;
            var pageGridSpacing = section is null ? 0f : ResolveGridSpacing(section.DocGrid);
            var bounds = page.Bounds;
            var rect = new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
            var pageColor = section?.PageBackgroundColor ?? options.PageColor;
            targetCanvas.DrawRect(rect, GetFillPaint(pageColor));

            var pageBorders = section?.PageBorders;
            var isFirstPageOfSection = section is null
                || pageIndex == 0
                || layout.PageSections[pageIndex - 1].SectionIndex != section.SectionIndex;
            var drawDocBorder = pageBorders is not null
                && pageBorders.HasAny
                && ShouldDrawPageBorder(pageBorders, isFirstPageOfSection);
            var drawUiBorder = !drawDocBorder && options.PageBorderThickness > 0f;
            var drawBorderInFront = drawDocBorder && pageBorders!.ZOrder == PageBorderZOrder.Front;

            if (drawDocBorder && !drawBorderInFront && section is not null)
            {
                DrawPageBorders(page, section, pageBorders!);
            }
            else if (drawUiBorder)
            {
                targetCanvas.DrawRect(rect, pageBorderPaint);
            }

            if (options.ColumnSeparatorThickness > 0f)
            {
                DrawColumnSeparators(page, pageIndex);
            }

            DrawHeaderFooterFloatingObjects(pageIndex, true);
            DrawFloatingObjects(pageIndex, true);

            if (headerFooterMap.TryGetValue(pageIndex, out var headerFooter))
            {
                foreach (var line in headerFooter.HeaderLines)
                {
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, pageGridSpacing);
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.Rubies, line.TextDirection, pageGridSpacing);
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.TextDirection, pageGridSpacing, false, 0f);
                }

                foreach (var line in headerFooter.FooterLines)
                {
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, pageGridSpacing);
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.Rubies, line.TextDirection, pageGridSpacing);
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.TextDirection, pageGridSpacing, false, 0f);
                }
            }

            foreach (var table in layout.Tables)
            {
                if (!IntersectsPage(bounds, table.Bounds))
                {
                    continue;
                }

                foreach (var cell in table.Cells)
                {
                    var cellBounds = cell.Bounds;
                    var cellRect = new SKRect(cellBounds.X, cellBounds.Y, cellBounds.Right, cellBounds.Bottom);
                    if (cell.Properties.ShadingColor is { } shading)
                    {
                        using var shadingPaint = new SKPaint
                        {
                            Style = SKPaintStyle.Fill,
                            Color = ToSkColor(shading),
                            IsAntialias = true
                        };
                        targetCanvas.DrawRect(cellRect, shadingPaint);
                    }

                    foreach (var line in cell.Lines)
                    {
                        var lineGridSpacing = ResolveLineGridSpacing(line.ParagraphIndex, pageIndex);
                        DrawCommentHighlights(line.ParagraphIndex, line.StartOffset, line.Length, line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, lineGridSpacing);
                        DrawLineHighlights(line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, lineGridSpacing);
                        if (selection.HasValue && TryGetSelectionSpan(selection.Value, line.ParagraphIndex, line.StartOffset, line.Length, out var startOffset, out var endOffset))
                        {
                            var selectionX1 = MeasureLineOffset(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, startOffset - line.StartOffset, lineGridSpacing, GetRunMetrics);
                            var selectionX2 = MeasureLineOffset(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, endOffset - line.StartOffset, lineGridSpacing, GetRunMetrics);
                            DrawLineRangeRect(line.X, line.Y, line.LineHeight, line.TextDirection, selectionX1, selectionX2, selectionPaint);
                        }

                        DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.Rubies, line.TextDirection, lineGridSpacing);
                        DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.TextDirection, lineGridSpacing, false, 0f);
                    }
                }

                DrawTableBorders(targetCanvas, table, GetBorderPaint);
            }

            var lineRange = layout.LineIndex.GetLineRangeForPage(pageIndex);
            var lineStart = Math.Clamp(lineRange.Start, 0, layout.Lines.Count);
            var lineEnd = Math.Clamp(lineRange.End, lineStart, layout.Lines.Count);
            var caretDrawn = false;

            DrawParagraphDecorations(lineStart, lineEnd);

            for (var lineIndex = lineStart; lineIndex < lineEnd; lineIndex++)
            {
                var line = layout.Lines[lineIndex];
                var isTableLine = line.IsInTable;
                var lineGridSpacing = ResolveLineGridSpacing(line.ParagraphIndex, pageIndex);
                if (!isTableLine
                    && lineIndex < lineNumbers.Length
                    && lineNumbers[lineIndex].HasValue
                    && section?.LineNumbering is not null)
                {
                    var lineNumberDistance = section.LineNumbering.Distance ?? 12f;
                    var numberText = lineNumbers[lineIndex]!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var numberWidth = lineNumberPaint.MeasureText(numberText);
                    var numberX = page.ContentBounds.X - lineNumberDistance - numberWidth;
                    var numberY = line.Y + line.Ascent;
                    targetCanvas.DrawText(numberText, numberX, numberY, lineNumberPaint);
                }
                if (!isTableLine)
                {
                    DrawCommentHighlights(line.ParagraphIndex, line.StartOffset, line.Length, line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, lineGridSpacing);
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, lineGridSpacing);
                    if (selection.HasValue && TryGetSelectionSpan(selection.Value, line, out var startOffset, out var endOffset))
                    {
                        var selectionX1 = MeasureLineOffset(line, startOffset - line.StartOffset, lineGridSpacing, GetRunMetrics);
                        var selectionX2 = MeasureLineOffset(line, endOffset - line.StartOffset, lineGridSpacing, GetRunMetrics);
                        DrawLineRangeRect(line.X, line.Y, line.LineHeight, line.TextDirection, selectionX1, selectionX2, selectionPaint);
                    }
                }

                if (!isTableLine)
                {
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.Rubies, line.TextDirection, lineGridSpacing);
                }

                var isLastLine = lineIndex == layout.Lines.Count - 1
                                 || layout.Lines[lineIndex + 1].ParagraphIndex != line.ParagraphIndex;
                if (!isTableLine)
                {
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.TextDirection, lineGridSpacing, isLastLine, line.Width);
                }

                if (!caretDrawn && options.ShowCaret && line.ParagraphIndex == options.Caret.ParagraphIndex)
                {
                    var lineStartOffset = line.StartOffset;
                    var lineEndOffset = line.StartOffset + line.Length;
                    if (options.Caret.Offset >= lineStartOffset && options.Caret.Offset <= lineEndOffset)
                    {
                        if (options.Caret.Offset != lineEndOffset || isLastLine)
                        {
                            var offsetInLine = Math.Clamp(options.Caret.Offset - line.StartOffset, 0, line.Length);
                            var caretX = MeasureLineOffset(line, offsetInLine, lineGridSpacing, GetRunMetrics);
                            DrawLineRangeRect(line.X, line.Y, line.LineHeight, line.TextDirection, caretX, caretX + options.CaretThickness, caretPaint);
                            caretDrawn = true;
                        }
                    }
                }
            }

            DrawBreakMarkers(pageIndex);

            if (footnoteMap.TryGetValue(pageIndex, out var footnoteLayout))
            {
                var separator = footnoteLayout.SeparatorBounds;
                if (separator.Width > 0f)
                {
                    targetCanvas.DrawLine(separator.Left, separator.Top, separator.Right, separator.Top, footnoteSeparatorPaint);
                }

                foreach (var line in footnoteLayout.Lines)
                {
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.TextDirection, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, pageGridSpacing);
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.Rubies, line.TextDirection, pageGridSpacing);
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, line.TextDirection, pageGridSpacing, false, 0f);
                }
            }

            DrawFloatingObjects(pageIndex, false);
            DrawHeaderFooterFloatingObjects(pageIndex, false);
            if (drawDocBorder && drawBorderInFront && section is not null)
            {
                DrawPageBorders(page, section, pageBorders!);
            }
            DrawLayoutGuides(pageIndex);
            DrawFloatingSelection(pageIndex);
        }

        void DrawLayoutGuides(int pageIndex)
        {
            if (!options.ShowLayout)
            {
                return;
            }

            if (pageIndex < 0 || pageIndex >= layout.Pages.Count || pageIndex >= layout.PageSections.Count)
            {
                return;
            }

            var page = layout.Pages[pageIndex];
            var section = layout.PageSections[pageIndex];
            var contentLeft = page.Bounds.X + section.MarginLeft;
            var contentTop = page.Bounds.Y + section.MarginTop;
            var contentWidth = MathF.Max(1f, page.Bounds.Width - section.MarginLeft - section.MarginRight);
            var contentHeight = MathF.Max(1f, page.Bounds.Height - section.MarginTop - section.MarginBottom);
            var contentRight = contentLeft + contentWidth;
            var contentBottom = contentTop + contentHeight;

            targetCanvas.DrawRect(new SKRect(contentLeft, contentTop, contentRight, contentBottom), layoutGuidePaint);

            var headerY = page.Bounds.Y + section.HeaderOffset;
            var footerY = page.Bounds.Bottom - section.FooterOffset;
            targetCanvas.DrawLine(contentLeft, headerY, contentRight, headerY, layoutGuideLightPaint);
            targetCanvas.DrawLine(contentLeft, footerY, contentRight, footerY, layoutGuideLightPaint);

            if (section.ColumnCount > 1)
            {
                var columnGap = MathF.Max(0f, section.ColumnGap);
                var columnGaps = ResolveSectionColumnGaps(section, Math.Max(1, section.ColumnCount), columnGap);
                var columnWidths = ResolveSectionColumnWidths(section, contentWidth, columnGaps);
                if (columnWidths.Length > 1)
                {
                    var columnOffsets = BuildColumnOffsets(columnWidths, columnGaps);
                    for (var i = 0; i < columnWidths.Length; i++)
                    {
                        var columnLeft = contentLeft + columnOffsets[i];
                        var columnRight = columnLeft + columnWidths[i];
                        targetCanvas.DrawRect(new SKRect(columnLeft, contentTop, columnRight, contentBottom), layoutGuideLightPaint);
                        if (i < columnWidths.Length - 1)
                        {
                            var separatorX = columnRight + columnGaps[i] * 0.5f;
                            targetCanvas.DrawLine(separatorX, contentTop, separatorX, contentBottom, layoutGuideLightPaint);
                        }
                    }
                }
            }

            foreach (var table in layout.Tables)
            {
                if (!IntersectsPage(page.Bounds, table.Bounds))
                {
                    continue;
                }

                var tableBounds = table.Bounds;
                targetCanvas.DrawRect(new SKRect(tableBounds.X, tableBounds.Y, tableBounds.Right, tableBounds.Bottom), layoutGuidePaint);
                foreach (var cell in table.Cells)
                {
                    var cellBounds = cell.Bounds;
                    targetCanvas.DrawRect(new SKRect(cellBounds.X, cellBounds.Y, cellBounds.Right, cellBounds.Bottom), layoutGuideLightPaint);
                }
            }

            foreach (var floating in layout.FloatingObjects)
            {
                if (floating.PageIndex != pageIndex)
                {
                    continue;
                }

                var bounds = floating.Bounds;
                targetCanvas.DrawRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom), layoutGuideLightPaint);
            }
        }

        void DrawBreakMarkers(int pageIndex)
        {
            if (!options.ShowInvisibles || layout.BreakMarkers.Count == 0)
            {
                return;
            }

            using var breakLinePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = ToSkColor(options.InvisiblesColor),
                StrokeWidth = 1f,
                IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0f)
            };

            var labelStyle = style.Clone();
            labelStyle.Color = options.InvisiblesColor;
            labelStyle.FontSize = MathF.Max(8f, style.FontSize * 0.85f);
            var labelPaint = GetInvisibleTextPaint(labelStyle);
            var metrics = labelPaint.FontMetrics;
            var textHeight = metrics.Descent - metrics.Ascent;
            var textPadding = MathF.Max(6f, textHeight * 0.4f);

            foreach (var marker in layout.BreakMarkers)
            {
                if (marker.PageIndex != pageIndex)
                {
                    continue;
                }

                var left = marker.X;
                var right = marker.X + marker.Width;
                if (right <= left)
                {
                    continue;
                }

                var lineY = marker.Y;
                var label = marker.Label;
                var labelWidth = string.IsNullOrWhiteSpace(label) ? 0f : labelPaint.MeasureText(label);
                var labelX = left + (right - left - labelWidth) / 2f;
                var labelBaseline = lineY - 2f;

                if (labelWidth > 0f)
                {
                    var gapLeft = MathF.Max(left, labelX - textPadding);
                    var gapRight = MathF.Min(right, labelX + labelWidth + textPadding);
                    if (gapLeft > left)
                    {
                        targetCanvas.DrawLine(left, lineY, gapLeft, lineY, breakLinePaint);
                    }
                    if (gapRight < right)
                    {
                        targetCanvas.DrawLine(gapRight, lineY, right, lineY, breakLinePaint);
                    }

                    targetCanvas.DrawText(label, labelX, labelBaseline, labelPaint);
                }
                else
                {
                    targetCanvas.DrawLine(left, lineY, right, lineY, breakLinePaint);
                }
            }
        }

        void DrawColumnSeparators(PageLayout page, int pageIndex)
        {
            if (layout.ParagraphSectionIndices.Count == 0)
            {
                return;
            }

            var lineRange = layout.LineIndex.GetLineRangeForPage(pageIndex);
            if (lineRange.Count == 0)
            {
                return;
            }

            var rangesBySection = new Dictionary<int, (float Top, float Bottom)>();
            for (var i = lineRange.Start; i < lineRange.End && i < layout.Lines.Count; i++)
            {
                var line = layout.Lines[i];
                if (line.ParagraphIndex < 0)
                {
                    continue;
                }

                if (!layout.ParagraphSectionIndices.TryGetValue(line.ParagraphIndex, out var sectionIndex))
                {
                    continue;
                }

                var top = line.Y;
                var bottom = line.Y + line.LineHeight;
                if (rangesBySection.TryGetValue(sectionIndex, out var range))
                {
                    rangesBySection[sectionIndex] = (MathF.Min(range.Top, top), MathF.Max(range.Bottom, bottom));
                }
                else
                {
                    rangesBySection[sectionIndex] = (top, bottom);
                }
            }

            foreach (var entry in rangesBySection)
            {
                if (!layout.SectionSettings.TryGetValue(entry.Key, out var section))
                {
                    continue;
                }

                var pageSection = section.ResolveForPage(pageIndex);

                if (!pageSection.ColumnSeparator || pageSection.ColumnCount <= 1)
                {
                    continue;
                }

                var contentLeft = page.Bounds.X + pageSection.MarginLeft;
                var contentTop = page.Bounds.Y + pageSection.MarginTop;
                var contentBottom = page.Bounds.Bottom - pageSection.MarginBottom;
                var top = MathF.Max(entry.Value.Top, contentTop);
                var bottom = MathF.Min(entry.Value.Bottom, contentBottom);
                if (bottom <= top)
                {
                    continue;
                }

                var columnGap = MathF.Max(0f, pageSection.ColumnGap);
                var contentWidth = MathF.Max(1f, page.Bounds.Width - pageSection.MarginLeft - pageSection.MarginRight);
                var columnGaps = ResolveSectionColumnGaps(pageSection, Math.Max(1, pageSection.ColumnCount), columnGap);
                var columnWidths = ResolveSectionColumnWidths(pageSection, contentWidth, columnGaps);
                if (columnWidths.Length <= 1)
                {
                    continue;
                }

                var columnOffsets = BuildColumnOffsets(columnWidths, columnGaps);
                for (var i = 0; i < columnWidths.Length - 1; i++)
                {
                    var gap = columnGaps[i];
                    var x = contentLeft + columnOffsets[i] + columnWidths[i] + gap * 0.5f;
                    targetCanvas.DrawLine(x, top, x, bottom, columnSeparatorPaint);
                }
            }
        }

        void DrawParagraphDecorations(int lineStart, int lineEnd)
        {
            if (lineEnd <= lineStart)
            {
                return;
            }

            var handled = new HashSet<int>();
            for (var lineIndex = lineStart; lineIndex < lineEnd; lineIndex++)
            {
                var line = layout.Lines[lineIndex];
                if (line.IsInTable)
                {
                    continue;
                }

                var paragraphIndex = line.ParagraphIndex;
                if (!handled.Add(paragraphIndex))
                {
                    continue;
                }

                if (!layout.ParagraphLineRanges.TryGetValue(paragraphIndex, out var range) || range.Count == 0)
                {
                    continue;
                }

                var segmentStart = Math.Clamp(Math.Max(range.Start, lineStart), lineStart, lineEnd);
                var segmentEnd = Math.Clamp(Math.Min(range.End, lineEnd), lineStart, lineEnd);
                if (segmentEnd <= segmentStart)
                {
                    continue;
                }

                var left = float.MaxValue;
                var right = float.MinValue;
                var top = float.MaxValue;
                var bottom = float.MinValue;

                for (var i = segmentStart; i < segmentEnd; i++)
                {
                    var segmentLine = layout.Lines[i];
                    if (segmentLine.IsInTable)
                    {
                        continue;
                    }

                    var lineLeft = segmentLine.X - (segmentLine.Prefix is null ? 0f : segmentLine.PrefixWidth);
                    var lineRight = segmentLine.X + segmentLine.Width;
                    left = MathF.Min(left, lineLeft);
                    right = MathF.Max(right, lineRight);
                    top = MathF.Min(top, segmentLine.Y);
                    bottom = MathF.Max(bottom, segmentLine.Y + segmentLine.LineHeight);
                }

                if (left >= right || top >= bottom)
                {
                    continue;
                }

                var paragraph = document.GetParagraph(paragraphIndex);
                var properties = styleResolver.ResolveParagraphProperties(paragraph);
                if (properties.ShadingColor is { } shading)
                {
                    using var shadingPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = ToSkColor(shading),
                        IsAntialias = true
                    };
                    targetCanvas.DrawRect(new SKRect(left, top, right, bottom), shadingPaint);
                }

                var borders = properties.Borders;
                if (borders.HasAny)
                {
                    var drawTop = range.Start >= lineStart && range.Start < lineEnd;
                    var drawBottom = range.End <= lineEnd && range.End > lineStart;

                    if (drawTop && borders.Top is not null && borders.Top.IsVisible)
                    {
                        DrawBorderSegment(targetCanvas, borders.Top, left, top, right, top, GetBorderPaint);
                    }

                    if (drawBottom && borders.Bottom is not null && borders.Bottom.IsVisible)
                    {
                        DrawBorderSegment(targetCanvas, borders.Bottom, left, bottom, right, bottom, GetBorderPaint);
                    }

                    if (borders.Left is not null && borders.Left.IsVisible)
                    {
                        DrawBorderSegment(targetCanvas, borders.Left, left, top, left, bottom, GetBorderPaint);
                    }

                    if (borders.Right is not null && borders.Right.IsVisible)
                    {
                        DrawBorderSegment(targetCanvas, borders.Right, right, top, right, bottom, GetBorderPaint);
                    }
                }
            }
        }

        var dirtyPages = options.DirtyPages;
        if (!ReferenceEquals(layout, _cachedLayout))
        {
            if (dirtyPages is null)
            {
                ClearPageCache();
            }
            else
            {
                TrimPageCache(layout.Pages.Count);
            }

            _cachedLayout = layout;
            _lastDirtyVersion = -1;
        }

        if (!options.UsePictureCache)
        {
            ClearPageCache();
            targetCanvas = canvas;
            for (var pageIndex = 0; pageIndex < layout.Pages.Count; pageIndex++)
            {
                RenderPage(pageIndex);
            }
        }
        else
        {
            var applyDirtyPages = dirtyPages is { Count: > 0 } && options.DirtyVersion != _lastDirtyVersion;
            HashSet<int>? dirtyPageSet = applyDirtyPages ? new HashSet<int>(dirtyPages!) : null;

            for (var pageIndex = 0; pageIndex < layout.Pages.Count; pageIndex++)
            {
                var needsRedraw = dirtyPageSet is not null && dirtyPageSet.Contains(pageIndex);
                _pageCache.TryGetValue(pageIndex, out var picture);
                if (needsRedraw || picture is null)
                {
                    picture?.Dispose();
                    using var recorder = new SKPictureRecorder();
                    var pageBounds = layout.Pages[pageIndex].Bounds;
                    var recordRect = new SKRect(pageBounds.X, pageBounds.Y, pageBounds.Right, pageBounds.Bottom);
                    targetCanvas = recorder.BeginRecording(recordRect);

                    RenderPage(pageIndex);

                    picture = recorder.EndRecording();
                    _pageCache[pageIndex] = picture;
                }

                if (picture is not null)
                {
                    canvas.DrawPicture(picture);
                }
            }

            if (applyDirtyPages)
            {
                _lastDirtyVersion = options.DirtyVersion;
            }
        }

        foreach (var paint in paintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in typefacePaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in highlightPaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in fillPaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in borderPaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in invisibleTextPaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var shaper in shaperCache.Values)
        {
            shaper.Dispose();
        }

        foreach (var shaper in typefaceShaperCache.Values)
        {
            shaper.Dispose();
        }
    }

    private void ClearPageCache()
    {
        foreach (var picture in _pageCache.Values)
        {
            picture.Dispose();
        }

        _pageCache.Clear();
    }

    private void TrimPageCache(int pageCount)
    {
        if (pageCount < 0)
        {
            pageCount = 0;
        }

        var keysToRemove = _pageCache.Keys.Where(key => key < 0 || key >= pageCount).ToList();
        foreach (var key in keysToRemove)
        {
            if (_pageCache.TryGetValue(key, out var picture))
            {
                picture.Dispose();
            }

            _pageCache.Remove(key);
        }
    }

    private static float MeasureLineOffset(LayoutLine line, int length, float gridSpacing, Func<string, TextStyle, float, float, RunMetrics> metricsProvider)
    {
        return MeasureLineOffset(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, length, gridSpacing, metricsProvider);
    }

    private static float MeasureLineOffset(
        ReadOnlySpan<char> lineText,
        bool baseRtl,
        IReadOnlyList<LayoutRun> runs,
        IReadOnlyList<LayoutImage> images,
        IReadOnlyList<LayoutShape> shapes,
        IReadOnlyList<LayoutChart> charts,
        IReadOnlyList<LayoutEquation> equations,
        int length,
        float gridSpacing,
        Func<string, TextStyle, float, float, RunMetrics> metricsProvider)
    {
        if (length <= 0)
        {
            return 0f;
        }

        var segments = BuildVisualSegments(lineText, baseRtl, runs, images, shapes, charts, equations, gridSpacing, metricsProvider);
        if (segments.Count == 0)
        {
            return 0f;
        }

        var totalWidth = segments[^1].X + segments[^1].Width;
        var target = Math.Clamp(length, 0, int.MaxValue);
        VisualSegment? containing = null;
        foreach (var segment in segments)
        {
            if (target == segment.StartOffset)
            {
                containing = segment;
                break;
            }

            if (containing is null && target > segment.StartOffset && target <= segment.StartOffset + segment.Length)
            {
                containing = segment;
            }
        }

        if (containing is not null)
        {
            var offsetInSegment = Math.Clamp(target - containing.StartOffset, 0, containing.Length);
            var localX = MeasureSegmentOffset(containing, offsetInSegment, gridSpacing, metricsProvider);
            return containing.X + localX;
        }

        return target <= 0 ? 0f : totalWidth;
    }

    private static float MeasureSegmentOffset(VisualSegment segment, int offsetInSegment, float gridSpacing, Func<string, TextStyle, float, float, RunMetrics> metricsProvider)
    {
        if (offsetInSegment <= 0)
        {
            return segment.IsRtl ? segment.Width : 0f;
        }

        float width;
        if (segment.IsText && segment.Run is not null)
        {
            var run = segment.Run;
            var metrics = metricsProvider(run.Text, run.Style, run.LetterSpacing, gridSpacing);
            var startWidth = metrics.GetWidth(segment.RunStart);
            var endWidth = metrics.GetWidth(segment.RunStart + offsetInSegment);
            width = MathF.Max(0f, endWidth - startWidth);
            if (segment.Scale != 1f)
            {
                width *= segment.Scale;
            }
        }
        else
        {
            width = offsetInSegment >= segment.Length ? segment.Width : 0f;
        }

        return segment.IsRtl ? segment.Width - width : width;
    }

    private static bool TryGetSelectionSpan(TextRange selection, LayoutLine line, out int startOffset, out int endOffset)
    {
        return TryGetSelectionSpan(selection, line.ParagraphIndex, line.StartOffset, line.Length, out startOffset, out endOffset);
    }

    private static bool TryGetSelectionSpan(TextRange selection, int paragraphIndex, int lineStartOffset, int lineLength, out int startOffset, out int endOffset)
    {
        startOffset = 0;
        endOffset = 0;

        var lineStart = new TextPosition(paragraphIndex, lineStartOffset);
        var lineEnd = new TextPosition(paragraphIndex, lineStartOffset + lineLength);

        if (selection.End <= lineStart || selection.Start >= lineEnd)
        {
            return false;
        }

        if (selection.Start.ParagraphIndex > paragraphIndex || selection.End.ParagraphIndex < paragraphIndex)
        {
            return false;
        }

        startOffset = lineStartOffset;
        endOffset = lineStartOffset + lineLength;

        if (selection.Start.ParagraphIndex == paragraphIndex)
        {
            startOffset = Math.Max(startOffset, selection.Start.Offset);
        }

        if (selection.End.ParagraphIndex == paragraphIndex)
        {
            endOffset = Math.Min(endOffset, selection.End.Offset);
        }

        return startOffset < endOffset;
    }

    private static SKColor ToSkColor(DocColor color) => new SKColor(color.R, color.G, color.B, color.A);

    private static float MeasureChar(SKPaint paint, char ch)
    {
        Span<char> buffer = stackalloc char[1];
        buffer[0] = ch;
        return paint.MeasureText(buffer);
    }

    private static float MeasureCluster(SKPaint paint, ReadOnlySpan<char> cluster)
    {
        return cluster.Length == 1 ? MeasureChar(paint, cluster[0]) : paint.MeasureText(cluster);
    }

    private static float MeasureTextElements(SKPaint paint, ReadOnlySpan<char> text, float letterSpacing)
    {
        if (text.IsEmpty)
        {
            return 0f;
        }

        var width = 0f;
        var index = 0;
        var applySpacing = MathF.Abs(letterSpacing) > 0.001f;
        while (index < text.Length)
        {
            var length = TextCluster.GetNextClusterLength(text, index);
            if (length <= 0)
            {
                break;
            }

            width += MeasureCluster(paint, text.Slice(index, length));
            if (applySpacing && index + length < text.Length)
            {
                width += letterSpacing;
            }

            index += length;
        }

        return width;
    }

    private static float SnapToGridForward(float value, float spacing)
    {
        if (spacing <= 0f)
        {
            return value;
        }

        return MathF.Ceiling(value / spacing) * spacing;
    }

    private static string GetClusterString(ReadOnlySpan<char> cluster)
    {
        if (cluster.Length == 1)
        {
            var ch = cluster[0];
            return ch < AsciiCharCache.Length ? AsciiCharCache[ch] : ch.ToString();
        }

        return cluster.ToString();
    }

    private static bool IsWhitespaceCluster(ReadOnlySpan<char> cluster)
    {
        if (cluster.IsEmpty)
        {
            return false;
        }

        for (var i = 0; i < cluster.Length; i++)
        {
            if (!char.IsWhiteSpace(cluster[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] BuildAsciiCharCache()
    {
        var cache = new string[128];
        for (var i = 0; i < cache.Length; i++)
        {
            cache[i] = ((char)i).ToString();
        }

        return cache;
    }

    private static void DrawUnderlineIfNeeded(SKCanvas canvas, float baseline, float lineX, LayoutRun run, SKPaint paint)
    {
        DrawUnderlineSpan(canvas, baseline, lineX + run.X, run.Width, run.Text, run.Style, run.LetterSpacing, paint);
    }

    private static void DrawUnderlineSpan(
        SKCanvas canvas,
        float baseline,
        float startX,
        float width,
        string text,
        TextStyle style,
        float letterSpacing,
        SKPaint paint)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var underlineStyle = style.UnderlineStyle;
        if (underlineStyle == DocUnderlineStyle.None && !style.Underline)
        {
            return;
        }

        if (underlineStyle == DocUnderlineStyle.None)
        {
            underlineStyle = DocUnderlineStyle.Single;
        }

        var metrics = paint.FontMetrics;
        var underlinePosition = metrics.UnderlinePosition ?? 0f;
        var underlineThickness = metrics.UnderlineThickness ?? 1f;
        if (underlineThickness <= 0f)
        {
            underlineThickness = 1f;
        }

        var thickness = AdjustUnderlineThickness(underlineStyle, underlineThickness);
        using var underlinePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = style.UnderlineColor.HasValue ? ToSkColor(style.UnderlineColor.Value) : paint.Color,
            StrokeWidth = thickness,
            IsAntialias = true
        };

        var endX = startX + width;
        var y = baseline + underlinePosition;

        if (underlineStyle == DocUnderlineStyle.Words)
        {
            var measuredWidth = MeasureTextElements(paint, text.AsSpan(), letterSpacing);
            var scale = measuredWidth > 0f ? width / measuredWidth : 1f;
            DrawUnderlineWords(canvas, text.AsSpan(), startX, y, underlinePaint, thickness, paint, letterSpacing, scale);
            return;
        }

        DrawUnderlineSegment(canvas, startX, endX, y, underlineStyle, underlinePaint, thickness);
    }

    private static void DrawTextWithSpacing(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        SKPaint paint,
        SKShaper? shaper,
        float letterSpacing,
        float gridSpacing,
        TextStyle style,
        SKPaint? measurePaint = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        measurePaint ??= paint;

        if (style.Effects?.HasValues == true)
        {
            DrawTextWithEffects(canvas, text, x, baseline, paint, measurePaint, shaper, letterSpacing, gridSpacing, style);
            return;
        }

        DrawTextWithSpacingCore(canvas, text, x, baseline, paint, measurePaint, shaper, letterSpacing, gridSpacing, style);
    }

    private static void DrawTextWithSpacingCore(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        SKPaint paint,
        SKPaint measurePaint,
        SKShaper? shaper,
        float letterSpacing,
        float gridSpacing,
        TextStyle style)
    {
        if (gridSpacing <= 0f)
        {
            if (MathF.Abs(letterSpacing) <= 0.001f)
            {
                if (shaper is null)
                {
                    canvas.DrawText(text, x, baseline, paint);
                }
                else
                {
                    canvas.DrawShapedText(shaper, text, x, baseline, paint);
                }
            }
            else
            {
                DrawTextWithLetterSpacing(canvas, text, x, baseline, paint, measurePaint, shaper, letterSpacing, style);
            }

            return;
        }

        DrawTextWithGridSpacing(canvas, text, x, baseline, paint, measurePaint, shaper, letterSpacing, gridSpacing, style);
    }

    private static void DrawTextWithEffects(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        SKPaint paint,
        SKPaint measurePaint,
        SKShaper? shaper,
        float letterSpacing,
        float gridSpacing,
        TextStyle style)
    {
        var effects = style.Effects;
        if (effects is null || !effects.HasValues)
        {
            DrawTextWithSpacingCore(canvas, text, x, baseline, paint, measurePaint, shaper, letterSpacing, gridSpacing, style);
            return;
        }

        if (effects.Shadow is { Enabled: true } shadow)
        {
            ResolveTextShadow(shadow, paint.TextSize, out var blurRadius, out var distance, out var direction);
            var angle = direction * (MathF.PI / 180f);
            var dx = distance * MathF.Cos(angle);
            var dy = distance * MathF.Sin(angle);
            using var shadowPaint = paint.Clone();
            shadowPaint.Color = ToSkColor(shadow.Color);
            shadowPaint.Style = SKPaintStyle.Fill;
            shadowPaint.MaskFilter = blurRadius > 0f ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius) : null;
            DrawTextWithSpacingCore(canvas, text, x + dx, baseline + dy, shadowPaint, measurePaint, shaper, letterSpacing, gridSpacing, style);
        }

        var emboss = effects.Emboss == true;
        var imprint = effects.Imprint == true;
        if (emboss || imprint)
        {
            var offset = MathF.Max(0.6f, paint.TextSize * 0.06f);
            var blur = MathF.Max(0.5f, paint.TextSize * 0.04f);
            var baseColor = paint.Color;
            var lightColor = BlendColor(baseColor, SKColors.White, 0.6f);
            var darkColor = BlendColor(baseColor, SKColors.Black, 0.6f);
            if (imprint)
            {
                (lightColor, darkColor) = (darkColor, lightColor);
            }

            var highlightOffset = imprint ? offset : -offset;
            var shadowOffset = imprint ? -offset : offset;

            using var highlightPaint = paint.Clone();
            highlightPaint.Color = lightColor;
            highlightPaint.Style = SKPaintStyle.Fill;
            highlightPaint.MaskFilter = blur > 0f ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur) : null;
            using var shadowPaint = paint.Clone();
            shadowPaint.Color = darkColor;
            shadowPaint.Style = SKPaintStyle.Fill;
            shadowPaint.MaskFilter = blur > 0f ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur) : null;
            DrawTextWithSpacingCore(canvas, text, x + highlightOffset, baseline + highlightOffset, highlightPaint, measurePaint, shaper, letterSpacing, gridSpacing, style);
            DrawTextWithSpacingCore(canvas, text, x + shadowOffset, baseline + shadowOffset, shadowPaint, measurePaint, shaper, letterSpacing, gridSpacing, style);
        }

        if (effects.Outline is not null && effects.Outline.Enabled)
        {
            var thickness = effects.Outline.Thickness ?? MathF.Max(0.6f, paint.TextSize * 0.06f);
            using var outlinePaint = paint.Clone();
            outlinePaint.Style = SKPaintStyle.Stroke;
            outlinePaint.StrokeWidth = MathF.Max(0.5f, thickness);
            outlinePaint.StrokeJoin = SKStrokeJoin.Round;
            outlinePaint.Color = effects.Outline.Color.HasValue ? ToSkColor(effects.Outline.Color.Value) : paint.Color;
            DrawTextWithSpacingCore(canvas, text, x, baseline, outlinePaint, measurePaint, shaper, letterSpacing, gridSpacing, style);
        }

        DrawTextWithSpacingCore(canvas, text, x, baseline, paint, measurePaint, shaper, letterSpacing, gridSpacing, style);
    }

    private static void ResolveTextShadow(TextShadowEffect shadow, float textSize, out float blurRadius, out float distance, out float direction)
    {
        blurRadius = shadow.BlurRadius;
        distance = shadow.Distance;
        direction = shadow.Direction;

        if (blurRadius <= 0f && distance <= 0f)
        {
            blurRadius = MathF.Max(1f, textSize * 0.08f);
            distance = MathF.Max(1f, textSize * 0.06f);
            if (MathF.Abs(direction) <= 0.01f)
            {
                direction = 45f;
            }
        }
        else
        {
            if (blurRadius <= 0f)
            {
                blurRadius = MathF.Max(0.6f, textSize * 0.05f);
            }

            if (distance < 0f)
            {
                distance = 0f;
            }
        }
    }

    private static SKColor BlendColor(SKColor baseColor, SKColor blend, float amount)
    {
        if (amount <= 0f)
        {
            return baseColor;
        }

        if (amount >= 1f)
        {
            return new SKColor(blend.Red, blend.Green, blend.Blue, baseColor.Alpha);
        }

        var r = (byte)MathF.Round(baseColor.Red + (blend.Red - baseColor.Red) * amount);
        var g = (byte)MathF.Round(baseColor.Green + (blend.Green - baseColor.Green) * amount);
        var b = (byte)MathF.Round(baseColor.Blue + (blend.Blue - baseColor.Blue) * amount);
        return new SKColor(r, g, b, baseColor.Alpha);
    }

    private static float Clamp01(float value)
    {
        if (value <= 0f)
        {
            return 0f;
        }

        if (value >= 1f)
        {
            return 1f;
        }

        return value;
    }

    private static SKRect ExpandRect(SKRect rect, float left, float top, float right, float bottom)
    {
        return new SKRect(rect.Left - left, rect.Top - top, rect.Right + right, rect.Bottom + bottom);
    }

    private static SKPaint CreateReflectionMaskPaint(SKRect rect, float startOpacity, float endOpacity)
    {
        var startAlpha = (byte)MathF.Round(Clamp01(startOpacity) * 255f);
        var endAlpha = (byte)MathF.Round(Clamp01(endOpacity) * 255f);
        var shader = SKShader.CreateLinearGradient(
            new SKPoint(0f, rect.Top),
            new SKPoint(0f, rect.Bottom),
            new[] { new SKColor(255, 255, 255, startAlpha), new SKColor(255, 255, 255, endAlpha) },
            null,
            SKShaderTileMode.Clamp);

        return new SKPaint
        {
            Shader = shader,
            BlendMode = SKBlendMode.DstIn,
            IsAntialias = true
        };
    }

    private static void DrawTextWithGridSpacing(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        SKPaint paint,
        SKPaint measurePaint,
        SKShaper? shaper,
        float letterSpacing,
        float gridSpacing,
        TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (shaper is null)
        {
            DrawTextWithGridSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing, gridSpacing);
            return;
        }

        try
        {
            using var buffer = SkiaTextMeasurer.CreateBuffer(text.AsSpan(), style);
            var result = shaper.Shape(buffer, measurePaint);
            var clusters = result.Clusters;
            var points = result.Points;
            var codepoints = result.Codepoints;
            if (clusters is null || points is null || codepoints is null)
            {
                DrawTextWithGridSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing, gridSpacing);
                return;
            }

            var shapeInfo = SkiaTextMeasurer.BuildShapeInfo(text.Length, result);
            if (shapeInfo.ClusterOffsets.Length == 0)
            {
                DrawTextWithGridSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing, gridSpacing);
                return;
            }

            var orderByCluster = new int[text.Length];
            Array.Fill(orderByCluster, -1);
            for (var i = 0; i < shapeInfo.ClusterOffsets.Length; i++)
            {
                var clusterIndex = shapeInfo.ClusterOffsets[i];
                if ((uint)clusterIndex < (uint)orderByCluster.Length)
                {
                    orderByCluster[clusterIndex] = i;
                }
            }

            var clusterCount = shapeInfo.ClusterOffsets.Length;
            var originalPositions = new float[clusterCount];
            var snappedPositions = new float[clusterCount];
            var originalTotal = 0f;
            var snappedTotal = 0f;
            var applySpacing = MathF.Abs(letterSpacing) > 0.001f;
            for (var i = 0; i < clusterCount; i++)
            {
                originalPositions[i] = originalTotal;
                snappedPositions[i] = snappedTotal;
                var advance = i < shapeInfo.ClusterAdvances.Length ? shapeInfo.ClusterAdvances[i] : 0f;
                if (applySpacing && i < clusterCount - 1)
                {
                    advance += letterSpacing;
                }

                originalTotal += advance;
                snappedTotal = SnapToGridForward(snappedTotal + advance, gridSpacing);
            }

            var glyphCount = Math.Min(codepoints.Length, Math.Min(points.Length, clusters.Length));
            if (glyphCount == 0)
            {
                DrawTextWithGridSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing, gridSpacing);
                return;
            }

            var positions = new SKPoint[glyphCount];
            var glyphs = new ushort[glyphCount];
            for (var i = 0; i < glyphCount; i++)
            {
                var clusterIndex = (int)clusters[i];
                var order = (uint)clusterIndex < (uint)orderByCluster.Length ? orderByCluster[clusterIndex] : -1;
                if (order < 0)
                {
                    order = 0;
                }

                var delta = snappedPositions[order] - originalPositions[order];
                var point = points[i];
                positions[i] = new SKPoint(point.X + delta, point.Y);

                var codepoint = codepoints[i];
                if (codepoint > ushort.MaxValue)
                {
                    DrawTextWithGridSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing, gridSpacing);
                    return;
                }

                glyphs[i] = (ushort)codepoint;
            }

            using var font = paint.ToFont();
            using var blobBuilder = new SKTextBlobBuilder();
            blobBuilder.AddPositionedRun(glyphs, font, positions);
            using var blob = blobBuilder.Build();
            if (blob is null)
            {
                DrawTextWithGridSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing, gridSpacing);
                return;
            }

            canvas.DrawText(blob, x, baseline, paint);
        }
        catch
        {
            DrawTextWithGridSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing, gridSpacing);
        }
    }

    private static void DrawTextWithGridSpacingFallback(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        SKPaint paint,
        SKPaint measurePaint,
        float letterSpacing,
        float gridSpacing)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var span = text.AsSpan();
        var offset = 0f;
        var index = 0;
        var applySpacing = MathF.Abs(letterSpacing) > 0.001f;
        while (index < span.Length)
        {
            var length = TextCluster.GetNextClusterLength(span, index);
            if (length <= 0)
            {
                break;
            }

            var cluster = span.Slice(index, length);
            var glyph = GetClusterString(cluster);
            canvas.DrawText(glyph, x + offset, baseline, paint);

            var advance = MeasureCluster(measurePaint, cluster);
            if (applySpacing && index + length < span.Length)
            {
                advance += letterSpacing;
            }

            offset = SnapToGridForward(offset + advance, gridSpacing);
            index += length;
        }
    }

    private static void DrawTextWithLetterSpacing(SKCanvas canvas, string text, float x, float baseline, SKPaint paint, SKPaint measurePaint, SKShaper? shaper, float letterSpacing, TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (shaper is null)
        {
            DrawTextWithLetterSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing);
            return;
        }

        try
        {
            using var buffer = SkiaTextMeasurer.CreateBuffer(text.AsSpan(), style);
            var result = shaper.Shape(buffer, measurePaint);
            var clusters = result.Clusters;
            var points = result.Points;
            var codepoints = result.Codepoints;
            if (clusters is null || points is null || codepoints is null)
            {
                DrawTextWithLetterSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing);
                return;
            }

            var shapeInfo = SkiaTextMeasurer.BuildShapeInfo(text.Length, result);
            if (shapeInfo.ClusterOffsets.Length == 0)
            {
                DrawTextWithLetterSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing);
                return;
            }

            var orderByCluster = new int[text.Length];
            Array.Fill(orderByCluster, -1);
            for (var i = 0; i < shapeInfo.ClusterOffsets.Length; i++)
            {
                var clusterIndex = shapeInfo.ClusterOffsets[i];
                if ((uint)clusterIndex < (uint)orderByCluster.Length)
                {
                    orderByCluster[clusterIndex] = i;
                }
            }

            var glyphCount = Math.Min(codepoints.Length, Math.Min(points.Length, clusters.Length));
            if (glyphCount == 0)
            {
                DrawTextWithLetterSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing);
                return;
            }

            var positions = new SKPoint[glyphCount];
            var glyphs = new ushort[glyphCount];
            for (var i = 0; i < glyphCount; i++)
            {
                var clusterIndex = (int)clusters[i];
                var order = (uint)clusterIndex < (uint)orderByCluster.Length ? orderByCluster[clusterIndex] : -1;
                if (order < 0)
                {
                    order = 0;
                }

                var point = points[i];
                var offsetX = letterSpacing * order;
                positions[i] = new SKPoint(point.X + offsetX, point.Y);

                var codepoint = codepoints[i];
                if (codepoint > ushort.MaxValue)
                {
                    DrawTextWithLetterSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing);
                    return;
                }

                glyphs[i] = (ushort)codepoint;
            }

            using var font = paint.ToFont();
            using var blobBuilder = new SKTextBlobBuilder();
            blobBuilder.AddPositionedRun(glyphs, font, positions);
            using var blob = blobBuilder.Build();
            if (blob is null)
            {
                DrawTextWithLetterSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing);
                return;
            }

            canvas.DrawText(blob, x, baseline, paint);
        }
        catch
        {
            DrawTextWithLetterSpacingFallback(canvas, text, x, baseline, paint, measurePaint, letterSpacing);
        }
    }

    private static void DrawTextWithLetterSpacingFallback(SKCanvas canvas, string text, float x, float baseline, SKPaint paint, SKPaint measurePaint, float letterSpacing)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var span = text.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            var length = TextCluster.GetNextClusterLength(span, index);
            if (length <= 0)
            {
                break;
            }

            var cluster = span.Slice(index, length);
            var glyph = GetClusterString(cluster);
            canvas.DrawText(glyph, x, baseline, paint);

            var advance = MeasureCluster(measurePaint, cluster);
            if (index + length < span.Length)
            {
                advance += letterSpacing;
            }

            x += advance;
            index += length;
        }
    }

    private static void DrawUnderlineWords(
        SKCanvas canvas,
        ReadOnlySpan<char> text,
        float startX,
        float y,
        SKPaint underlinePaint,
        float thickness,
        SKPaint textPaint,
        float letterSpacing,
        float scale)
    {
        if (text.IsEmpty)
        {
            return;
        }

        var x = startX;
        var segmentStart = -1f;
        var applySpacing = MathF.Abs(letterSpacing) > 0.001f;
        var index = 0;
        while (index < text.Length)
        {
            var length = TextCluster.GetNextClusterLength(text, index);
            if (length <= 0)
            {
                break;
            }

            var cluster = text.Slice(index, length);
            var width = MeasureCluster(textPaint, cluster) * scale;
            var isWhitespace = IsWhitespaceCluster(cluster);
            if (isWhitespace)
            {
                if (segmentStart >= 0f)
                {
                    DrawUnderlineSegment(canvas, segmentStart, x, y, DocUnderlineStyle.Single, underlinePaint, thickness);
                    segmentStart = -1f;
                }

                x += width;
                index += length;
                continue;
            }

            if (segmentStart < 0f)
            {
                segmentStart = x;
            }

            x += width;
            if (applySpacing && index + length < text.Length)
            {
                x += letterSpacing * scale;
            }

            index += length;
        }

        if (segmentStart >= 0f)
        {
            DrawUnderlineSegment(canvas, segmentStart, x, y, DocUnderlineStyle.Single, underlinePaint, thickness);
        }
    }

    private static void DrawUnderlineSegment(SKCanvas canvas, float startX, float endX, float y, DocUnderlineStyle style, SKPaint underlinePaint, float thickness)
    {
        if (endX <= startX)
        {
            return;
        }

        var effectiveStyle = style;
        if (effectiveStyle == DocUnderlineStyle.WavyDouble)
        {
            var gap = thickness * 2.2f;
            DrawWavyUnderline(canvas, startX, endX, y, thickness, underlinePaint);
            DrawWavyUnderline(canvas, startX, endX, y + gap, thickness, underlinePaint);
            return;
        }

        if (effectiveStyle == DocUnderlineStyle.Wave || effectiveStyle == DocUnderlineStyle.WavyHeavy)
        {
            DrawWavyUnderline(canvas, startX, endX, y, thickness, underlinePaint);
            return;
        }

        underlinePaint.PathEffect = CreateUnderlineEffect(effectiveStyle, thickness);

        if (effectiveStyle == DocUnderlineStyle.Double)
        {
            var gap = thickness * 2.2f;
            canvas.DrawLine(startX, y, endX, y, underlinePaint);
            canvas.DrawLine(startX, y + gap, endX, y + gap, underlinePaint);
            return;
        }

        canvas.DrawLine(startX, y, endX, y, underlinePaint);
    }

    private static void DrawWavyUnderline(SKCanvas canvas, float startX, float endX, float y, float thickness, SKPaint underlinePaint)
    {
        var amplitude = MathF.Max(1f, thickness * 0.9f);
        var wavelength = MathF.Max(6f, thickness * 6f);
        var step = MathF.Max(2f, wavelength / 4f);
        using var path = new SKPath();
        var x = startX;
        path.MoveTo(x, y);
        while (x < endX)
        {
            var progress = (x - startX) / wavelength * MathF.PI * 2f;
            var offset = MathF.Sin(progress) * amplitude;
            path.LineTo(x, y + offset);
            x += step;
        }

        path.LineTo(endX, y);
        canvas.DrawPath(path, underlinePaint);
    }

    private static float AdjustUnderlineThickness(DocUnderlineStyle style, float baseThickness)
    {
        return style switch
        {
            DocUnderlineStyle.Thick or DocUnderlineStyle.DottedHeavy or DocUnderlineStyle.DashedHeavy
                or DocUnderlineStyle.DashLongHeavy or DocUnderlineStyle.DashDotHeavy or DocUnderlineStyle.DashDotDotHeavy
                or DocUnderlineStyle.WavyHeavy => MathF.Max(1f, baseThickness * 1.6f),
            _ => MathF.Max(0.5f, baseThickness)
        };
    }

    private static SKPathEffect? CreateUnderlineEffect(DocUnderlineStyle style, float thickness)
    {
        var unit = MathF.Max(1f, thickness);
        return style switch
        {
            DocUnderlineStyle.Dotted => SKPathEffect.CreateDash(new[] { unit, unit }, 0),
            DocUnderlineStyle.DottedHeavy => SKPathEffect.CreateDash(new[] { unit, unit }, 0),
            DocUnderlineStyle.Dash => SKPathEffect.CreateDash(new[] { unit * 4f, unit * 2f }, 0),
            DocUnderlineStyle.DashedHeavy => SKPathEffect.CreateDash(new[] { unit * 5f, unit * 2f }, 0),
            DocUnderlineStyle.DashLong => SKPathEffect.CreateDash(new[] { unit * 6f, unit * 2f }, 0),
            DocUnderlineStyle.DashLongHeavy => SKPathEffect.CreateDash(new[] { unit * 7f, unit * 2.5f }, 0),
            DocUnderlineStyle.DotDash => SKPathEffect.CreateDash(new[] { unit, unit, unit * 4f, unit * 2f }, 0),
            DocUnderlineStyle.DotDotDash => SKPathEffect.CreateDash(new[] { unit, unit, unit, unit, unit * 4f, unit * 2f }, 0),
            _ => null
        };
    }

    private static void DrawStrikeThroughIfNeeded(SKCanvas canvas, float baseline, float lineX, LayoutRun run, SKPaint paint)
    {
        DrawStrikeThroughSpan(canvas, baseline, lineX + run.X, run.Width, run.Text, run.Style, paint);
    }

    private static void DrawStrikeThroughSpan(
        SKCanvas canvas,
        float baseline,
        float startX,
        float width,
        string text,
        TextStyle style,
        SKPaint paint)
    {
        if (!style.Strikethrough || string.IsNullOrEmpty(text))
        {
            return;
        }

        var thickness = MathF.Max(1f, paint.TextSize / 14f);
        var metrics = paint.FontMetrics;
        var ascent = MathF.Max(1f, -metrics.Ascent);
        var y = baseline - ascent * 0.4f;
        using var strikePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = paint.Color,
            StrokeWidth = thickness,
            IsAntialias = true
        };

        var endX = startX + width;
        canvas.DrawLine(startX, y, endX, y, strikePaint);
    }

    private static IEnumerable<LineSegment> EnumerateSegments(LayoutLine line)
    {
        return EnumerateSegments(line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
    }

    private static IEnumerable<LineSegment> EnumerateSegments(IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images, IReadOnlyList<LayoutShape> shapes, IReadOnlyList<LayoutChart> charts, IReadOnlyList<LayoutEquation> equations)
    {
        var runIndex = 0;
        var imageIndex = 0;
        var shapeIndex = 0;
        var chartIndex = 0;
        var equationIndex = 0;

        while (true)
        {
            var hasRun = TryPeekRun(runs, ref runIndex, out var run);
            var hasImage = imageIndex < images.Count;
            var hasShape = shapeIndex < shapes.Count;
            var hasChart = chartIndex < charts.Count;
            var hasEquation = equationIndex < equations.Count;

            if (!hasRun && !hasImage && !hasShape && !hasChart && !hasEquation)
            {
                yield break;
            }

            var kind = SegmentKind.None;
            var minX = float.PositiveInfinity;
            if (hasRun)
            {
                minX = run.X;
                kind = SegmentKind.Run;
            }

            if (hasImage && images[imageIndex].X < minX)
            {
                minX = images[imageIndex].X;
                kind = SegmentKind.Image;
            }

            if (hasShape && shapes[shapeIndex].X < minX)
            {
                minX = shapes[shapeIndex].X;
                kind = SegmentKind.Shape;
            }

            if (hasChart && charts[chartIndex].X < minX)
            {
                minX = charts[chartIndex].X;
                kind = SegmentKind.Chart;
            }

            if (hasEquation && equations[equationIndex].X < minX)
            {
                kind = SegmentKind.Equation;
            }

            switch (kind)
            {
                case SegmentKind.Run:
                    yield return run.IsTab
                        ? LineSegment.Tab(run.Width)
                        : LineSegment.CreateText(run.Text, run.Style, run.Width, run.Length, run.LetterSpacing);
                    runIndex++;
                    break;
                case SegmentKind.Image:
                    yield return LineSegment.Image(images[imageIndex].Width);
                    imageIndex++;
                    break;
                case SegmentKind.Shape:
                    yield return LineSegment.Shape(shapes[shapeIndex].Width);
                    shapeIndex++;
                    break;
                case SegmentKind.Chart:
                    yield return LineSegment.Chart(charts[chartIndex].Width);
                    chartIndex++;
                    break;
                case SegmentKind.Equation:
                    yield return LineSegment.Equation(equations[equationIndex].Width);
                    equationIndex++;
                    break;
            }
        }
    }

    private static List<VisualSegment> BuildVisualSegments(
        ReadOnlySpan<char> lineText,
        bool baseRtl,
        IReadOnlyList<LayoutRun> runs,
        IReadOnlyList<LayoutImage> images,
        IReadOnlyList<LayoutShape> shapes,
        IReadOnlyList<LayoutChart> charts,
        IReadOnlyList<LayoutEquation> equations,
        float gridSpacing,
        Func<string, TextStyle, float, float, RunMetrics> metricsProvider)
    {
        var logicalSegments = new List<LogicalSegment>(runs.Count + images.Count + shapes.Count + charts.Count + equations.Count);
        foreach (var run in runs)
        {
            if (run.Length <= 0)
            {
                continue;
            }

            logicalSegments.Add(LogicalSegment.FromRun(run));
        }

        foreach (var image in images)
        {
            if (image.Length <= 0)
            {
                continue;
            }

            logicalSegments.Add(LogicalSegment.FromImage(image));
        }

        foreach (var shape in shapes)
        {
            if (shape.Length <= 0)
            {
                continue;
            }

            logicalSegments.Add(LogicalSegment.FromShape(shape));
        }

        foreach (var chart in charts)
        {
            if (chart.Length <= 0)
            {
                continue;
            }

            logicalSegments.Add(LogicalSegment.FromChart(chart));
        }

        foreach (var equation in equations)
        {
            if (equation.Length <= 0)
            {
                continue;
            }

            logicalSegments.Add(LogicalSegment.FromEquation(equation));
        }

        if (logicalSegments.Count == 0)
        {
            return new List<VisualSegment>();
        }

        logicalSegments.Sort((left, right) => left.X.CompareTo(right.X));

        var offset = 0;
        for (var i = 0; i < logicalSegments.Count; i++)
        {
            logicalSegments[i].StartOffset = offset;
            offset += logicalSegments[i].Length;
        }

        var bidiSpans = TextBidi.GetBidiSpans(lineText, baseRtl);
        if (bidiSpans.Count == 0)
        {
            bidiSpans.Add(new BidiSpan(0, lineText.Length, baseRtl ? 1 : 0));
        }

        var segments = new List<VisualSegment>();
        var spanIndex = 0;

        foreach (var logical in logicalSegments)
        {
            var segmentStart = logical.StartOffset;
            var segmentEnd = segmentStart + logical.Length;
            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            while (spanIndex < bidiSpans.Count && bidiSpans[spanIndex].Start + bidiSpans[spanIndex].Length <= segmentStart)
            {
                spanIndex++;
            }

            var scanIndex = spanIndex;
            while (scanIndex < bidiSpans.Count)
            {
                var span = bidiSpans[scanIndex];
                var spanStart = span.Start;
                var spanEnd = span.Start + span.Length;
                if (spanStart >= segmentEnd)
                {
                    break;
                }

                var overlapStart = Math.Max(segmentStart, spanStart);
                var overlapEnd = Math.Min(segmentEnd, spanEnd);
                var overlapLength = overlapEnd - overlapStart;
                if (overlapLength <= 0)
                {
                    scanIndex++;
                    continue;
                }

                if (logical.Run is not null)
                {
                    var run = logical.Run;
                    if (run.IsTab)
                    {
                        segments.Add(new VisualSegment(overlapStart, overlapLength, span.Level, logical.Width, 1f, run, 0, null, null, null, null));
                    }
                    else
                    {
                        var runStart = overlapStart - segmentStart;
                        var metricsWidth = MeasureRunSegmentWidth(run, runStart, overlapLength, gridSpacing, metricsProvider, out var scale);
                        segments.Add(new VisualSegment(overlapStart, overlapLength, span.Level, metricsWidth, scale, run, runStart, null, null, null, null));
                    }
                }
                else if (logical.Image is not null)
                {
                    segments.Add(new VisualSegment(overlapStart, overlapLength, span.Level, logical.Width, 1f, null, 0, logical.Image, null, null, null));
                }
                else if (logical.Shape is not null)
                {
                    segments.Add(new VisualSegment(overlapStart, overlapLength, span.Level, logical.Width, 1f, null, 0, null, logical.Shape, null, null));
                }
                else if (logical.Chart is not null)
                {
                    segments.Add(new VisualSegment(overlapStart, overlapLength, span.Level, logical.Width, 1f, null, 0, null, null, logical.Chart, null));
                }
                else if (logical.Equation is not null)
                {
                    segments.Add(new VisualSegment(overlapStart, overlapLength, span.Level, logical.Width, 1f, null, 0, null, null, null, logical.Equation));
                }

                scanIndex++;
            }
        }

        if (segments.Count == 0)
        {
            return segments;
        }

        var baseLevel = baseRtl ? 1 : 0;
        TextBidi.ReorderByLevels(segments, segment => segment.Level, baseLevel);

        var x = 0f;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            segment.X = x;
            x += segment.Width;
        }

        return segments;
    }

    private static float MeasureRunSegmentWidth(LayoutRun run, int segmentStart, int segmentLength, float gridSpacing, Func<string, TextStyle, float, float, RunMetrics> metricsProvider, out float scale)
    {
        scale = 1f;
        if (string.IsNullOrEmpty(run.Text) || segmentLength <= 0)
        {
            return 0f;
        }

        var metrics = metricsProvider(run.Text, run.Style, run.LetterSpacing, gridSpacing);
        var runLength = run.Text.Length;
        if (segmentLength >= runLength)
        {
            if (metrics.Width > 0f && MathF.Abs(run.Width - metrics.Width) > 0.01f)
            {
                scale = run.Width / metrics.Width;
                return run.Width;
            }

            return metrics.Width;
        }

        var startWidth = metrics.GetWidth(segmentStart);
        var endWidth = metrics.GetWidth(segmentStart + segmentLength);
        var width = endWidth - startWidth;
        if (metrics.Width > 0f && MathF.Abs(run.Width - metrics.Width) > 0.01f)
        {
            scale = run.Width / metrics.Width;
            width *= scale;
        }

        return width;
    }

    private sealed class VisualSegment
    {
        public int StartOffset { get; }
        public int Length { get; }
        public int Level { get; }
        public float Width { get; }
        public float Scale { get; }
        public float X { get; set; }
        public LayoutRun? Run { get; }
        public int RunStart { get; }
        public LayoutImage? Image { get; }
        public LayoutShape? Shape { get; }
        public LayoutChart? Chart { get; }
        public LayoutEquation? Equation { get; }

        public VisualSegment(
            int startOffset,
            int length,
            int level,
            float width,
            float scale,
            LayoutRun? run,
            int runStart,
            LayoutImage? image,
            LayoutShape? shape,
            LayoutChart? chart,
            LayoutEquation? equation)
        {
            StartOffset = startOffset;
            Length = length;
            Level = level;
            Width = width;
            Scale = scale;
            Run = run;
            RunStart = runStart;
            Image = image;
            Shape = shape;
            Chart = chart;
            Equation = equation;
        }

        public bool IsRtl => (Level & 1) != 0;
        public bool IsTab => Run?.IsTab == true;
        public bool IsText => Run is not null && !Run.IsTab;
        public bool IsImage => Image is not null;
        public bool IsShape => Shape is not null;
        public bool IsChart => Chart is not null;
        public bool IsEquation => Equation is not null;
    }

    private sealed class LogicalSegment
    {
        public float X { get; }
        public int Length { get; }
        public float Width { get; }
        public LayoutRun? Run { get; }
        public LayoutImage? Image { get; }
        public LayoutShape? Shape { get; }
        public LayoutChart? Chart { get; }
        public LayoutEquation? Equation { get; }
        public int StartOffset { get; set; }

        private LogicalSegment(
            float x,
            int length,
            float width,
            LayoutRun? run,
            LayoutImage? image,
            LayoutShape? shape,
            LayoutChart? chart,
            LayoutEquation? equation)
        {
            X = x;
            Length = length;
            Width = width;
            Run = run;
            Image = image;
            Shape = shape;
            Chart = chart;
            Equation = equation;
        }

        public static LogicalSegment FromRun(LayoutRun run)
        {
            return new LogicalSegment(run.X, run.Length, run.Width, run, null, null, null, null);
        }

        public static LogicalSegment FromImage(LayoutImage image)
        {
            return new LogicalSegment(image.X, image.Length, image.Width, null, image, null, null, null);
        }

        public static LogicalSegment FromShape(LayoutShape shape)
        {
            return new LogicalSegment(shape.X, shape.Length, shape.Width, null, null, shape, null, null);
        }

        public static LogicalSegment FromChart(LayoutChart chart)
        {
            return new LogicalSegment(chart.X, chart.Length, chart.Width, null, null, null, chart, null);
        }

        public static LogicalSegment FromEquation(LayoutEquation equation)
        {
            return new LogicalSegment(equation.X, equation.Length, equation.Width, null, null, null, null, equation);
        }
    }

    private static bool TryPeekRun(IReadOnlyList<LayoutRun> runs, ref int index, out LayoutRun run)
    {
        while (index < runs.Count)
        {
            run = runs[index];
            if (run.IsTab || !string.IsNullOrEmpty(run.Text))
            {
                return true;
            }

            index++;
        }

        run = null!;
        return false;
    }

    private enum SegmentKind
    {
        None,
        Run,
        Image,
        Shape,
        Chart,
        Equation
    }

    // Rotation helpers are centralized in DocTextDirectionHelpers.

    private static float[] ResolveSectionColumnWidths(PageSectionSettings section, float contentWidth, IReadOnlyList<float> columnGaps)
    {
        var columnCount = Math.Max(1, section.ColumnCount);
        var gapTotal = 0f;
        for (var i = 0; i < columnGaps.Count; i++)
        {
            gapTotal += columnGaps[i];
        }

        var availableWidth = MathF.Max(1f, contentWidth - gapTotal);

        if (section.ColumnEqualWidth || section.ColumnWidths.Count == 0)
        {
            var width = availableWidth / columnCount;
            var widths = new float[columnCount];
            Array.Fill(widths, width);
            return widths;
        }

        var resolved = new float[columnCount];
        var explicitCount = Math.Min(section.ColumnWidths.Count, columnCount);
        var explicitTotal = 0f;
        for (var i = 0; i < explicitCount; i++)
        {
            resolved[i] = section.ColumnWidths[i];
            explicitTotal += resolved[i];
        }

        if (explicitCount < columnCount)
        {
            var remaining = MathF.Max(0f, availableWidth - explicitTotal);
            var autoWidth = remaining / Math.Max(1, columnCount - explicitCount);
            for (var i = explicitCount; i < columnCount; i++)
            {
                resolved[i] = autoWidth;
            }
        }

        var total = resolved.Sum();
        if (total <= 0f)
        {
            var width = availableWidth / columnCount;
            Array.Fill(resolved, width);
            return resolved;
        }

        var delta = availableWidth - total;
        if (MathF.Abs(delta) > 0.01f)
        {
            var lastIndex = resolved.Length - 1;
            if (resolved[lastIndex] + delta > 0f)
            {
                resolved[lastIndex] += delta;
            }
            else if (availableWidth > 0f)
            {
                var scale = availableWidth / total;
                for (var i = 0; i < resolved.Length; i++)
                {
                    resolved[i] *= scale;
                }
            }
        }

        return resolved;
    }

    private static float[] ResolveSectionColumnGaps(PageSectionSettings section, int columnCount, float defaultGap)
    {
        if (columnCount <= 1)
        {
            return Array.Empty<float>();
        }

        var gaps = new float[columnCount - 1];
        if (section.ColumnGaps.Count == 0)
        {
            Array.Fill(gaps, MathF.Max(0f, defaultGap));
            return gaps;
        }

        for (var i = 0; i < gaps.Length; i++)
        {
            var gap = i < section.ColumnGaps.Count ? section.ColumnGaps[i] : float.NaN;
            if (float.IsNaN(gap))
            {
                gap = defaultGap;
            }

            gaps[i] = MathF.Max(0f, gap);
        }

        return gaps;
    }

    private static float[] BuildColumnOffsets(float[] widths, IReadOnlyList<float> gaps)
    {
        if (widths.Length == 0)
        {
            return Array.Empty<float>();
        }

        var offsets = new float[widths.Length];
        var current = 0f;
        for (var i = 0; i < widths.Length; i++)
        {
            offsets[i] = current;
            current += widths[i];
            if (i < widths.Length - 1)
            {
                current += gaps[i];
            }
        }

        return offsets;
    }

    private readonly Dictionary<Guid, SKBitmap> _imageCache = new();
    private readonly HashSet<Guid> _invalidImages = new();

    private readonly struct LineSegment
    {
        public string Text { get; }
        public TextStyle Style { get; }
        public float Width { get; }
        public int Length { get; }
        public float LetterSpacing { get; }
        public bool IsTab { get; }
        public bool IsImage { get; }
        public bool IsShape { get; }
        public bool IsChart { get; }
        public bool IsEquation { get; }

        private LineSegment(string text, TextStyle style, float width, int length, float letterSpacing, bool isTab, bool isImage, bool isShape, bool isChart, bool isEquation)
        {
            Text = text;
            Style = style;
            Width = width;
            Length = length;
            LetterSpacing = letterSpacing;
            IsTab = isTab;
            IsImage = isImage;
            IsShape = isShape;
            IsChart = isChart;
            IsEquation = isEquation;
        }

        public static LineSegment CreateText(string text, TextStyle style, float width, int length, float letterSpacing)
        {
            return new LineSegment(text, style, width, length, letterSpacing, false, false, false, false, false);
        }

        public static LineSegment Tab(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, 0f, true, false, false, false, false);
        }

        public static LineSegment Image(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, 0f, false, true, false, false, false);
        }

        public static LineSegment Shape(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, 0f, false, false, true, false, false);
        }

        public static LineSegment Chart(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, 0f, false, false, false, true, false);
        }

        public static LineSegment Equation(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, 0f, false, false, false, false, true);
        }
    }

    private readonly struct TextStyleKey : IEquatable<TextStyleKey>
    {
        private readonly string _fontFamily;
        private readonly float _fontSize;
        private readonly DocFontWeight _fontWeight;
        private readonly DocFontStyle _fontStyle;
        private readonly DocColor _color;
        private readonly bool _underline;
        private readonly bool _strikethrough;
        private readonly bool _hasHighlight;
        private readonly DocColor _highlight;
        private readonly string _language;
        private readonly string _languageEastAsia;
        private readonly string _languageBidi;

        public TextStyleKey(TextStyle style)
        {
            _fontFamily = style.FontFamily ?? string.Empty;
            _fontSize = style.FontSize;
            _fontWeight = style.FontWeight;
            _fontStyle = style.FontStyle;
            _color = style.Color;
            _underline = style.Underline;
            _strikethrough = style.Strikethrough;
            _hasHighlight = style.HighlightColor.HasValue;
            _highlight = style.HighlightColor ?? default;
            _language = style.Language ?? string.Empty;
            _languageEastAsia = style.LanguageEastAsia ?? string.Empty;
            _languageBidi = style.LanguageBidi ?? string.Empty;
        }

        public bool Equals(TextStyleKey other)
        {
            return _fontFamily == other._fontFamily
                && _fontSize.Equals(other._fontSize)
                && _fontWeight == other._fontWeight
                && _fontStyle == other._fontStyle
                && _color.Equals(other._color)
                && _underline == other._underline
                && _strikethrough == other._strikethrough
                && _hasHighlight == other._hasHighlight
                && (!_hasHighlight || _highlight.Equals(other._highlight))
                && _language == other._language
                && _languageEastAsia == other._languageEastAsia
                && _languageBidi == other._languageBidi;
        }

        public override bool Equals(object? obj) => obj is TextStyleKey other && Equals(other);
        public override int GetHashCode()
        {
            var hash = HashCode.Combine(
                _fontFamily,
                _fontSize,
                (int)_fontWeight,
                (int)_fontStyle,
                _color,
                _underline,
                _strikethrough,
                _hasHighlight ? _highlight.GetHashCode() : 0);
            hash = HashCode.Combine(hash, _language);
            hash = HashCode.Combine(hash, _languageEastAsia);
            return HashCode.Combine(hash, _languageBidi);
        }
    }

    private readonly struct RunMetricsKey : IEquatable<RunMetricsKey>
    {
        private readonly string _text;
        private readonly TextStyleKey _styleKey;
        private readonly float _letterSpacing;
        private readonly float _gridSpacing;

        public RunMetricsKey(string text, TextStyle style, float letterSpacing, float gridSpacing)
        {
            _text = text;
            _styleKey = new TextStyleKey(style);
            _letterSpacing = letterSpacing;
            _gridSpacing = gridSpacing;
        }

        public bool Equals(RunMetricsKey other)
        {
            return _text == other._text
                && _styleKey.Equals(other._styleKey)
                && _letterSpacing.Equals(other._letterSpacing)
                && _gridSpacing.Equals(other._gridSpacing);
        }

        public override bool Equals(object? obj) => obj is RunMetricsKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_text, _styleKey, _letterSpacing, _gridSpacing);
    }

    private sealed class RunMetrics
    {
        public static readonly RunMetrics Empty = new RunMetrics(new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>()), 0f, 0f);
        private readonly int _textLength;
        private readonly int[] _clusterOffsets;
        private readonly float[] _clusterPositions;
        private readonly float _totalWidth;

        public RunMetrics(TextShapeInfo shape, float letterSpacing, float gridSpacing)
        {
            _textLength = Math.Max(0, shape.TextLength);
            _clusterOffsets = shape.ClusterOffsets.Length == 0 ? Array.Empty<int>() : shape.ClusterOffsets;
            _clusterPositions = new float[_clusterOffsets.Length];

            var total = 0f;
            for (var i = 0; i < _clusterOffsets.Length; i++)
            {
                _clusterPositions[i] = total;
                var advance = i < shape.ClusterAdvances.Length ? shape.ClusterAdvances[i] : 0f;
                if (letterSpacing != 0f && i < _clusterOffsets.Length - 1)
                {
                    advance += letterSpacing;
                }

                total = gridSpacing > 0f
                    ? SnapToGridForward(total + advance, gridSpacing)
                    : total + advance;
            }

            _totalWidth = total;
        }

        public float Width => _totalWidth;

        public float GetWidth(int length)
        {
            if (length <= 0 || _clusterOffsets.Length == 0)
            {
                return 0f;
            }

            if (length >= _textLength)
            {
                return Width;
            }

            var index = GetClusterIndexForOffset(length);
            if (index <= 0)
            {
                return _clusterPositions[0];
            }

            return _clusterPositions[index];
        }

        private int GetClusterIndexForOffset(int offset)
        {
            if (_clusterOffsets.Length == 0)
            {
                return 0;
            }

            var low = 0;
            var high = _clusterOffsets.Length - 1;
            while (low < high)
            {
                var mid = low + (high - low + 1) / 2;
                if (_clusterOffsets[mid] <= offset)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return low;
        }
    }
}

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
        var borderPaintCache = new Dictionary<BorderPaintKey, SKPaint>();
        var invisibleTextPaintCache = new Dictionary<TextStyleKey, SKPaint>();
        var shaperCache = new Dictionary<TextStyleKey, SKShaper>();
        var canShapeText = options.UseHarfBuzz;

        SKPaint GetPaint(TextStyle runStyle)
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

        SKShaper? GetShaper(TextStyle runStyle)
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
                var paint = GetPaint(runStyle);
                var typeface = paint.Typeface ?? SKTypeface.Default;
                var shaper = new SKShaper(typeface);
                shaperCache[key] = shaper;
                return shaper;
            }
            catch
            {
                canShapeText = false;
                foreach (var shaper in shaperCache.Values)
                {
                    shaper.Dispose();
                }

                shaperCache.Clear();
                return null;
            }
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

        using var pagePaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.PageColor),
            IsAntialias = true
        };

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

        void DrawLineHighlights(float lineX, float lineY, float lineHeight, IReadOnlyList<LayoutRun> runs)
        {
            foreach (var run in runs)
            {
                if (run.IsTab || string.IsNullOrEmpty(run.Text) || run.Style.HighlightColor is null)
                {
                    continue;
                }

                var highlightPaint = GetHighlightPaint(run.Style.HighlightColor.Value);
                var rect = new SKRect(lineX + run.X, lineY, lineX + run.X + run.Width, lineY + lineHeight);
                targetCanvas.DrawRect(rect, highlightPaint);
            }
        }

        void DrawCommentHighlights(int paragraphIndex, int lineStart, int lineLength, float lineX, float lineY, float lineHeight, IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images, IReadOnlyList<LayoutShape> shapes, IReadOnlyList<LayoutChart> charts, IReadOnlyList<LayoutEquation> equations)
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
                var highlightX1 = lineX + MeasureLineOffset(runs, images, shapes, charts, equations, startOffset, GetPaint);
                var highlightX2 = lineX + MeasureLineOffset(runs, images, shapes, charts, equations, endOffset, GetPaint);
                if (highlightX2 <= highlightX1)
                {
                    continue;
                }

                var rect = new SKRect(highlightX1, lineY, highlightX2, lineY + lineHeight);
                targetCanvas.DrawRect(rect, highlightPaint);
            }
        }

        void DrawLineContent(float lineX, float lineY, float lineHeight, float lineAscent, string? prefix, float prefixWidth, IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images, IReadOnlyList<LayoutShape> shapes, IReadOnlyList<LayoutChart> charts, IReadOnlyList<LayoutEquation> equations)
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                var prefixX = lineX - prefixWidth;
                var prefixBaseline = lineY + lineAscent;
                var prefixShaper = GetShaper(style);
                if (prefixShaper is null)
                {
                    targetCanvas.DrawText(prefix, prefixX, prefixBaseline, defaultPaint);
                }
                else
                {
                    targetCanvas.DrawShapedText(prefixShaper, prefix, prefixX, prefixBaseline, defaultPaint);
                }
            }

            var baseline = lineY + lineAscent;
            foreach (var run in runs)
            {
                if (run.IsTab)
                {
                    if (run.TabLeader != TabLeader.None && run.Width > 0f)
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
                            var paint = GetPaint(run.Style);
                            var glyphWidth = paint.MeasureText(leaderChar.ToString());
                            if (glyphWidth > 0f)
                            {
                                var count = Math.Max(1, (int)MathF.Ceiling(run.Width / glyphWidth));
                                var text = new string(leaderChar, count);
                                var startX = lineX + run.X;
                                var clipRect = new SKRect(startX, lineY, startX + run.Width, lineY + lineHeight);
                                targetCanvas.Save();
                                targetCanvas.ClipRect(clipRect);
                                targetCanvas.DrawText(text, startX, baseline, paint);
                                targetCanvas.Restore();
                            }
                        }
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                var runBaseline = baseline - run.BaselineOffset;
                var runPaint = GetPaint(run.Style);
                var shaper = GetShaper(run.Style);
                if (shaper is null)
                {
                    targetCanvas.DrawText(run.Text, lineX + run.X, runBaseline, runPaint);
                }
                else
                {
                    targetCanvas.DrawShapedText(shaper, run.Text, lineX + run.X, runBaseline, runPaint);
                }
                DrawUnderlineIfNeeded(targetCanvas, runBaseline, lineX, run, runPaint);
                DrawStrikeThroughIfNeeded(targetCanvas, runBaseline, lineX, run, runPaint);
            }

            foreach (var image in images)
            {
                DrawImage(targetCanvas, image, lineX, baseline, lineAscent, options);
            }

            foreach (var shape in shapes)
            {
                DrawShape(targetCanvas, shape, lineX, baseline, lineAscent, options, style);
            }

            foreach (var chart in charts)
            {
                DrawChart(targetCanvas, chart, lineX, baseline, options);
            }

            foreach (var equation in equations)
            {
                DrawEquation(targetCanvas, equation, lineX, baseline, GetPaint, GetShaper);
            }
        }

        void DrawLineInvisibles(float lineX, float lineY, float lineHeight, float lineAscent, IReadOnlyList<LayoutRun> runs, bool showParagraphMark, float paragraphMarkX)
        {
            if (!options.ShowInvisibles)
            {
                return;
            }

            var baseline = lineY + lineAscent;
            var dotY = baseline - lineAscent * 0.2f;
            var arrowSize = MathF.Max(4f, lineAscent * 0.3f);

            foreach (var run in runs)
            {
                if (run.IsTab)
                {
                    var startX = lineX + run.X;
                    var endX = lineX + run.X + run.Width;
                    var lineEnd = MathF.Max(startX, endX - arrowSize);
                    targetCanvas.DrawLine(startX, baseline, lineEnd, baseline, invisiblesStrokePaint);
                    targetCanvas.DrawLine(lineEnd, baseline, lineEnd - arrowSize * 0.6f, baseline - arrowSize * 0.4f, invisiblesStrokePaint);
                    targetCanvas.DrawLine(lineEnd, baseline, lineEnd - arrowSize * 0.6f, baseline + arrowSize * 0.4f, invisiblesStrokePaint);
                    continue;
                }

                if (string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                var paint = GetPaint(run.Style);
                var x = lineX + run.X;
                foreach (var ch in run.Text)
                {
                    var glyphWidth = paint.MeasureText(ch.ToString());
                    var advance = glyphWidth;
                    if (ch == ' ' && run.Text.Length == 1 && run.Width > glyphWidth + 0.01f)
                    {
                        advance = run.Width;
                    }

                    if (ch == ' ' || ch == '\u00A0')
                    {
                        var dotX = x + advance / 2f;
                        targetCanvas.DrawCircle(dotX, dotY, 1.3f, invisiblesFillPaint);
                    }

                    x += advance;
                }
            }

            if (showParagraphMark)
            {
                var markStyle = runs.LastOrDefault(run => !run.IsTab && !string.IsNullOrEmpty(run.Text))?.Style ?? style;
                var markPaint = GetInvisibleTextPaint(markStyle);
                targetCanvas.DrawText("¶", paragraphMarkX + 2f, baseline, markPaint);
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

        var headerFooterMap = layout.HeaderFooters.ToDictionary(item => item.PageIndex);

        static bool IntersectsPage(DocRect pageBounds, DocRect elementBounds)
        {
            return elementBounds.Bottom > pageBounds.Y && elementBounds.Y < pageBounds.Bottom;
        }

        void RenderPage(int pageIndex)
        {
            var page = layout.Pages[pageIndex];
            var bounds = page.Bounds;
            var rect = new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
            targetCanvas.DrawRect(rect, pagePaint);

            if (options.PageBorderThickness > 0)
            {
                targetCanvas.DrawRect(rect, pageBorderPaint);
            }

            if (options.ColumnSeparatorThickness > 0f)
            {
                DrawColumnSeparators(page, pageIndex);
            }

            DrawFloatingObjects(pageIndex, true);

            if (headerFooterMap.TryGetValue(pageIndex, out var headerFooter))
            {
                foreach (var line in headerFooter.HeaderLines)
                {
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.Runs);
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.Runs, false, 0f);
                }

                foreach (var line in headerFooter.FooterLines)
                {
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.Runs);
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.Runs, false, 0f);
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
                        DrawCommentHighlights(line.ParagraphIndex, line.StartOffset, line.Length, line.X, line.Y, line.LineHeight, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
                        DrawLineHighlights(line.X, line.Y, line.LineHeight, line.Runs);
                        if (selection.HasValue && TryGetSelectionSpan(selection.Value, line.ParagraphIndex, line.StartOffset, line.Length, out var startOffset, out var endOffset))
                        {
                            var selectionX1 = line.X + MeasureLineOffset(line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, startOffset - line.StartOffset, GetPaint);
                            var selectionX2 = line.X + MeasureLineOffset(line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, endOffset - line.StartOffset, GetPaint);
                            var selectionRect = new SKRect(selectionX1, line.Y, selectionX2, line.Y + line.LineHeight);
                            targetCanvas.DrawRect(selectionRect, selectionPaint);
                        }

                        DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
                        DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.Runs, false, 0f);
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
                if (!isTableLine)
                {
                    DrawCommentHighlights(line.ParagraphIndex, line.StartOffset, line.Length, line.X, line.Y, line.LineHeight, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.Runs);
                    if (selection.HasValue && TryGetSelectionSpan(selection.Value, line, out var startOffset, out var endOffset))
                    {
                        var selectionX1 = line.X + MeasureLineOffset(line, startOffset - line.StartOffset, GetPaint);
                        var selectionX2 = line.X + MeasureLineOffset(line, endOffset - line.StartOffset, GetPaint);
                        var selectionRect = new SKRect(selectionX1, line.Y, selectionX2, line.Y + line.LineHeight);
                        targetCanvas.DrawRect(selectionRect, selectionPaint);
                    }
                }

                if (!isTableLine)
                {
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
                }

                var isLastLine = lineIndex == layout.Lines.Count - 1
                                 || layout.Lines[lineIndex + 1].ParagraphIndex != line.ParagraphIndex;
                if (!isTableLine)
                {
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.Runs, isLastLine, line.X + line.Width);
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
                            var caretX = line.X + MeasureLineOffset(line, offsetInLine, GetPaint);
                            var caretRect = new SKRect(caretX, line.Y, caretX + options.CaretThickness, line.Y + line.LineHeight);
                            targetCanvas.DrawRect(caretRect, caretPaint);
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
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.Runs);
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.Runs, false, 0f);
                }
            }

            DrawFloatingObjects(pageIndex, false);
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
                var columnWidths = ResolveSectionColumnWidths(section, contentWidth, columnGap);
                if (columnWidths.Length > 1)
                {
                    var columnOffsets = BuildColumnOffsets(columnWidths, columnGap);
                    for (var i = 0; i < columnWidths.Length; i++)
                    {
                        var columnLeft = contentLeft + columnOffsets[i];
                        var columnRight = columnLeft + columnWidths[i];
                        targetCanvas.DrawRect(new SKRect(columnLeft, contentTop, columnRight, contentBottom), layoutGuideLightPaint);
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
                var columnWidths = ResolveSectionColumnWidths(pageSection, contentWidth, columnGap);
                if (columnWidths.Length <= 1)
                {
                    continue;
                }

                var columnOffsets = BuildColumnOffsets(columnWidths, columnGap);
                for (var i = 0; i < columnWidths.Length - 1; i++)
                {
                    var x = contentLeft + columnOffsets[i] + columnWidths[i] + columnGap * 0.5f;
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

        foreach (var paint in highlightPaintCache.Values)
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

    private static float MeasureLineOffset(LayoutLine line, int length, Func<TextStyle, SKPaint> paintProvider)
    {
        return MeasureLineOffset(line.Runs, line.Images, line.Shapes, line.Charts, line.Equations, length, paintProvider);
    }

    private static float MeasureLineOffset(IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images, IReadOnlyList<LayoutShape> shapes, IReadOnlyList<LayoutChart> charts, IReadOnlyList<LayoutEquation> equations, int length, Func<TextStyle, SKPaint> paintProvider)
    {
        if (length <= 0)
        {
            return 0f;
        }

        var remaining = length;
        var width = 0f;

        foreach (var segment in EnumerateSegments(runs, images, shapes, charts, equations))
        {
            if (remaining <= 0)
            {
                break;
            }

            if (segment.IsImage || segment.IsShape || segment.IsChart || segment.IsEquation)
            {
                if (remaining >= segment.Length)
                {
                    width += segment.Width;
                    remaining -= segment.Length;
                }

                continue;
            }

            if (segment.IsTab)
            {
                if (remaining >= segment.Length)
                {
                    width += segment.Width;
                    remaining -= segment.Length;
                }

                continue;
            }

            var take = Math.Min(remaining, segment.Length);
            if (take > 0)
            {
                if (segment.Width > 0f && segment.Length == 1)
                {
                    width += segment.Width;
                    remaining -= take;
                    continue;
                }

                var paint = paintProvider(segment.Style);
                if (segment.Width > 0f && take == segment.Length)
                {
                    var measured = paint.MeasureText(segment.Text);
                    width += MathF.Abs(segment.Width - measured) > 0.01f ? segment.Width : measured;
                }
                else
                {
                    width += paint.MeasureText(segment.Text.Substring(0, take));
                }
                remaining -= take;
            }
        }

        return width;
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

    private static void DrawUnderlineIfNeeded(SKCanvas canvas, float baseline, float lineX, LayoutRun run, SKPaint paint)
    {
        if (string.IsNullOrEmpty(run.Text))
        {
            return;
        }

        var underlineStyle = run.Style.UnderlineStyle;
        if (underlineStyle == DocUnderlineStyle.None && !run.Style.Underline)
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
            Color = run.Style.UnderlineColor.HasValue ? ToSkColor(run.Style.UnderlineColor.Value) : paint.Color,
            StrokeWidth = thickness,
            IsAntialias = true
        };

        var startX = lineX + run.X;
        var endX = lineX + run.X + run.Width;
        var y = baseline + underlinePosition;

        if (underlineStyle == DocUnderlineStyle.Words)
        {
            DrawUnderlineWords(canvas, run.Text, startX, y, underlinePaint, thickness, paint);
            return;
        }

        DrawUnderlineSegment(canvas, startX, endX, y, underlineStyle, underlinePaint, thickness);
    }

    private static void DrawUnderlineWords(SKCanvas canvas, string text, float startX, float y, SKPaint underlinePaint, float thickness, SKPaint textPaint)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var x = startX;
        var segmentStart = -1f;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var width = textPaint.MeasureText(ch.ToString());
            var isWhitespace = char.IsWhiteSpace(ch);
            if (isWhitespace)
            {
                if (segmentStart >= 0f)
                {
                    DrawUnderlineSegment(canvas, segmentStart, x, y, DocUnderlineStyle.Single, underlinePaint, thickness);
                    segmentStart = -1f;
                }

                x += width;
                continue;
            }

            if (segmentStart < 0f)
            {
                segmentStart = x;
            }

            x += width;
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
        if (!run.Style.Strikethrough || string.IsNullOrEmpty(run.Text))
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

        var startX = lineX + run.X;
        var endX = lineX + run.X + run.Width;
        canvas.DrawLine(startX, y, endX, y, strikePaint);
    }

    private static IEnumerable<LineSegment> EnumerateSegments(LayoutLine line)
    {
        return EnumerateSegments(line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
    }

    private static IEnumerable<LineSegment> EnumerateSegments(IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images, IReadOnlyList<LayoutShape> shapes, IReadOnlyList<LayoutChart> charts, IReadOnlyList<LayoutEquation> equations)
    {
        var segments = new List<(float X, LineSegment Segment)>();
        foreach (var run in runs)
        {
            if (run.IsTab)
            {
                segments.Add((run.X, LineSegment.Tab(run.Width)));
            }
            else if (!string.IsNullOrEmpty(run.Text))
            {
                segments.Add((run.X, LineSegment.CreateText(run.Text, run.Style, run.Width)));
            }
        }

        foreach (var image in images)
        {
            segments.Add((image.X, LineSegment.Image(image.Width)));
        }

        foreach (var shape in shapes)
        {
            segments.Add((shape.X, LineSegment.Shape(shape.Width)));
        }

        foreach (var chart in charts)
        {
            segments.Add((chart.X, LineSegment.Chart(chart.Width)));
        }

        foreach (var equation in equations)
        {
            segments.Add((equation.X, LineSegment.Equation(equation.Width)));
        }

        foreach (var segment in segments.OrderBy(item => item.X))
        {
            yield return segment.Segment;
        }
    }

    private static float[] ResolveSectionColumnWidths(PageSectionSettings section, float contentWidth, float columnGap)
    {
        var columnCount = Math.Max(1, section.ColumnCount);
        var availableWidth = MathF.Max(1f, contentWidth - columnGap * MathF.Max(0, columnCount - 1));

        if (section.ColumnEqualWidth || section.ColumnWidths.Count == 0)
        {
            var width = availableWidth / columnCount;
            var widths = new float[columnCount];
            Array.Fill(widths, width);
            return widths;
        }

        var resolved = new float[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            resolved[i] = i < section.ColumnWidths.Count ? section.ColumnWidths[i] : section.ColumnWidths.Last();
        }

        var total = resolved.Sum();
        if (total <= 0f)
        {
            var width = availableWidth / columnCount;
            Array.Fill(resolved, width);
            return resolved;
        }

        if (total > availableWidth && availableWidth > 0f)
        {
            var scale = availableWidth / total;
            for (var i = 0; i < resolved.Length; i++)
            {
                resolved[i] *= scale;
            }
        }

        return resolved;
    }

    private static float[] BuildColumnOffsets(float[] widths, float gap)
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
                current += gap;
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
        public bool IsTab { get; }
        public bool IsImage { get; }
        public bool IsShape { get; }
        public bool IsChart { get; }
        public bool IsEquation { get; }

        private LineSegment(string text, TextStyle style, float width, int length, bool isTab, bool isImage, bool isShape, bool isChart, bool isEquation)
        {
            Text = text;
            Style = style;
            Width = width;
            Length = length;
            IsTab = isTab;
            IsImage = isImage;
            IsShape = isShape;
            IsChart = isChart;
            IsEquation = isEquation;
        }

        public static LineSegment CreateText(string text, TextStyle style, float width)
        {
            return new LineSegment(text, style, width, text.Length, false, false, false, false, false);
        }

        public static LineSegment Tab(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, true, false, false, false, false);
        }

        public static LineSegment Image(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, false, true, false, false, false);
        }

        public static LineSegment Shape(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, false, false, true, false, false);
        }

        public static LineSegment Chart(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, false, false, false, true, false);
        }

        public static LineSegment Equation(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, false, false, false, false, true);
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
                && (!_hasHighlight || _highlight.Equals(other._highlight));
        }

        public override bool Equals(object? obj) => obj is TextStyleKey other && Equals(other);
        public override int GetHashCode()
        {
            return HashCode.Combine(
                _fontFamily,
                _fontSize,
                (int)_fontWeight,
                (int)_fontStyle,
                _color,
                _underline,
                _strikethrough,
                _hasHighlight ? _highlight.GetHashCode() : 0);
        }
    }
}

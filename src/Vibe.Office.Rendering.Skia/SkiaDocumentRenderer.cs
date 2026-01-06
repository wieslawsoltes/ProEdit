using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Office.Rendering;

namespace Vibe.Office.Rendering.Skia;

public sealed class SkiaDocumentRenderer : IDocumentRenderer<SKCanvas>
{
    private DocumentLayout? _cachedLayout;
    private readonly Dictionary<int, SKPicture> _pageCache = new();
    private long _lastDirtyVersion = -1;

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

            var paint = SkiaTextMeasurer.CreatePaint(runStyle);
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

            var paint = SkiaTextMeasurer.CreatePaint(runStyle);
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

        using var defaultPaint = SkiaTextMeasurer.CreatePaint(style);
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

        using var selectionPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSkColor(options.SelectionColor),
            IsAntialias = true
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

        void DrawCommentHighlights(int paragraphIndex, int lineStart, int lineLength, float lineX, float lineY, float lineHeight, IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images)
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
                var highlightX1 = lineX + MeasureLineOffset(runs, images, startOffset, GetPaint);
                var highlightX2 = lineX + MeasureLineOffset(runs, images, endOffset, GetPaint);
                if (highlightX2 <= highlightX1)
                {
                    continue;
                }

                var rect = new SKRect(highlightX1, lineY, highlightX2, lineY + lineHeight);
                targetCanvas.DrawRect(rect, highlightPaint);
            }
        }

        void DrawLineContent(float lineX, float lineY, float lineHeight, float lineAscent, string? prefix, float prefixWidth, IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images)
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
                if (run.IsTab || string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                var runBaseline = baseline - run.BaselineOffset;
                var paint = GetPaint(run.Style);
                var shaper = GetShaper(run.Style);
                if (shaper is null)
                {
                    targetCanvas.DrawText(run.Text, lineX + run.X, runBaseline, paint);
                }
                else
                {
                    targetCanvas.DrawShapedText(shaper, run.Text, lineX + run.X, runBaseline, paint);
                }
                DrawUnderlineIfNeeded(targetCanvas, runBaseline, lineX, run, paint);
                DrawStrikeThroughIfNeeded(targetCanvas, runBaseline, lineX, run, paint);
            }

            foreach (var image in images)
            {
                DrawImage(targetCanvas, image, lineX, baseline, lineAscent);
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
                    if (ch == ' ' || ch == '\u00A0')
                    {
                        var dotX = x + glyphWidth / 2f;
                        targetCanvas.DrawCircle(dotX, dotY, 1.3f, invisiblesFillPaint);
                    }

                    x += glyphWidth;
                }
            }

            if (showParagraphMark)
            {
                var markStyle = runs.LastOrDefault(run => !run.IsTab && !string.IsNullOrEmpty(run.Text))?.Style ?? style;
                var markPaint = GetInvisibleTextPaint(markStyle);
                targetCanvas.DrawText("¶", paragraphMarkX + 2f, baseline, markPaint);
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

            if (headerFooterMap.TryGetValue(pageIndex, out var headerFooter))
            {
                foreach (var line in headerFooter.HeaderLines)
                {
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.Runs);
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images);
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.Runs, false, 0f);
                }

                foreach (var line in headerFooter.FooterLines)
                {
                    DrawLineHighlights(line.X, line.Y, line.LineHeight, line.Runs);
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images);
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
                        DrawCommentHighlights(line.ParagraphIndex, line.StartOffset, line.Length, line.X, line.Y, line.LineHeight, line.Runs, line.Images);
                        DrawLineHighlights(line.X, line.Y, line.LineHeight, line.Runs);
                        if (selection.HasValue && TryGetSelectionSpan(selection.Value, line.ParagraphIndex, line.StartOffset, line.Length, out var startOffset, out var endOffset))
                        {
                            var selectionX1 = line.X + MeasureLineOffset(line.Runs, line.Images, startOffset - line.StartOffset, GetPaint);
                            var selectionX2 = line.X + MeasureLineOffset(line.Runs, line.Images, endOffset - line.StartOffset, GetPaint);
                            var selectionRect = new SKRect(selectionX1, line.Y, selectionX2, line.Y + line.LineHeight);
                            targetCanvas.DrawRect(selectionRect, selectionPaint);
                        }

                        DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images);
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
                    DrawCommentHighlights(line.ParagraphIndex, line.StartOffset, line.Length, line.X, line.Y, line.LineHeight, line.Runs, line.Images);
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
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images);
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
                    DrawLineContent(line.X, line.Y, line.LineHeight, line.Ascent, line.Prefix, line.PrefixWidth, line.Runs, line.Images);
                    DrawLineInvisibles(line.X, line.Y, line.LineHeight, line.Ascent, line.Runs, false, 0f);
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
        return MeasureLineOffset(line.Runs, line.Images, length, paintProvider);
    }

    private static float MeasureLineOffset(IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images, int length, Func<TextStyle, SKPaint> paintProvider)
    {
        if (length <= 0)
        {
            return 0f;
        }

        var remaining = length;
        var width = 0f;

        foreach (var segment in EnumerateSegments(runs, images))
        {
            if (remaining <= 0)
            {
                break;
            }

            if (segment.IsImage)
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
                var paint = paintProvider(segment.Style);
                width += paint.MeasureText(segment.Text.Substring(0, take));
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

    private static void DrawTableBorders(SKCanvas canvas, TableLayout table, Func<BorderLine, float, SKPaint> paintProvider)
    {
        if (table.Cells.Count == 0)
        {
            return;
        }

        var rowCount = table.Rows;
        var colCount = table.Columns;
        if (rowCount == 0 || colCount == 0)
        {
            return;
        }

        var rowTops = new float[rowCount];
        var rowBottoms = new float[rowCount];
        var y = table.Bounds.Y;
        for (var row = 0; row < rowCount; row++)
        {
            rowTops[row] = y;
            y += row < table.RowHeights.Count ? table.RowHeights[row] : 0f;
            rowBottoms[row] = y;
        }

        var colLefts = new float[colCount];
        var colRights = new float[colCount];
        var x = table.Bounds.X;
        for (var col = 0; col < colCount; col++)
        {
            colLefts[col] = x;
            x += col < table.ColumnWidths.Count ? table.ColumnWidths[col] : 0f;
            colRights[col] = x;
        }

        var minRow = table.Cells.Count == 0 ? 0 : table.Cells.Min(cell => cell.RowIndex);
        var grid = new TableCellLayout[rowCount, colCount];
        foreach (var cell in table.Cells)
        {
            var localRow = cell.RowIndex - minRow;
            if (localRow < 0 || localRow >= rowCount)
            {
                continue;
            }

            var rowSpan = Math.Max(1, cell.RowSpan);
            var colSpan = Math.Max(1, cell.ColumnSpan);
            for (var r = 0; r < rowSpan && localRow + r < rowCount; r++)
            {
                for (var c = 0; c < colSpan && cell.ColumnIndex + c < colCount; c++)
                {
                    grid[localRow + r, cell.ColumnIndex + c] = cell;
                }
            }
        }

        var tableBorders = table.Properties.Borders;

        for (var col = 0; col <= colCount; col++)
        {
            var lineX = col == colCount ? colRights[colCount - 1] : colLefts[col];
            for (var row = 0; row < rowCount; row++)
            {
                var leftCell = col == 0 ? null : grid[row, col - 1];
                var rightCell = col == colCount ? null : grid[row, col];
                if (leftCell is not null && rightCell is not null && ReferenceEquals(leftCell, rightCell))
                {
                    continue;
                }

                var border = col switch
                {
                    0 => ResolveBorderLine(rightCell?.Properties.Borders.Left, null, tableBorders.Left),
                    _ when col == colCount => ResolveBorderLine(leftCell?.Properties.Borders.Right, null, tableBorders.Right),
                    _ => ResolveBorderLine(leftCell?.Properties.Borders.Right, rightCell?.Properties.Borders.Left, tableBorders.InsideVertical)
                };

                if (border is null || !border.IsVisible)
                {
                    continue;
                }

                DrawBorderSegment(canvas, border, lineX, rowTops[row], lineX, rowBottoms[row], paintProvider);
            }
        }

        for (var row = 0; row <= rowCount; row++)
        {
            var lineY = row == rowCount ? rowBottoms[rowCount - 1] : rowTops[row];
            for (var col = 0; col < colCount; col++)
            {
                var upperCell = row == 0 ? null : grid[row - 1, col];
                var lowerCell = row == rowCount ? null : grid[row, col];
                if (upperCell is not null && lowerCell is not null && ReferenceEquals(upperCell, lowerCell))
                {
                    continue;
                }

                var border = row switch
                {
                    0 => ResolveBorderLine(lowerCell?.Properties.Borders.Top, null, tableBorders.Top),
                    _ when row == rowCount => ResolveBorderLine(upperCell?.Properties.Borders.Bottom, null, tableBorders.Bottom),
                    _ => ResolveBorderLine(upperCell?.Properties.Borders.Bottom, lowerCell?.Properties.Borders.Top, tableBorders.InsideHorizontal)
                };

                if (border is null || !border.IsVisible)
                {
                    continue;
                }

                DrawBorderSegment(canvas, border, colLefts[col], lineY, colRights[col], lineY, paintProvider);
            }
        }
    }

    private static BorderLine? ResolveBorderLine(BorderLine? primary, BorderLine? secondary, BorderLine? fallback = null)
    {
        var primarySet = primary is not null;
        var secondarySet = secondary is not null;
        if (!primarySet && !secondarySet)
        {
            return fallback is { IsVisible: true } ? fallback : null;
        }

        var resolved = ResolveBorderLine(primary, secondary);
        return resolved is { IsVisible: true } ? resolved : null;
    }

    private static BorderLine? ResolveBorderLine(BorderLine? primary, BorderLine? secondary)
    {
        var primaryVisible = primary?.IsVisible == true;
        var secondaryVisible = secondary?.IsVisible == true;
        if (!primaryVisible && !secondaryVisible)
        {
            return primary ?? secondary;
        }

        if (!secondaryVisible)
        {
            return primary;
        }

        if (!primaryVisible)
        {
            return secondary;
        }

        var primaryWeight = GetBorderWeight(primary!);
        var secondaryWeight = GetBorderWeight(secondary!);
        if (primaryWeight > secondaryWeight)
        {
            return primary;
        }

        if (secondaryWeight > primaryWeight)
        {
            return secondary;
        }

        return primary;
    }

    private static void DrawBorderSegment(
        SKCanvas canvas,
        BorderLine border,
        float x1,
        float y1,
        float x2,
        float y2,
        Func<BorderLine, float, SKPaint> paintProvider)
    {
        var thickness = GetBorderThickness(border);
        if (border.Style == DocBorderStyle.Double)
        {
            var lineThickness = MathF.Max(0.5f, thickness / 2f);
            var gap = border.Spacing ?? lineThickness;
            var offset = (lineThickness + gap) / 2f;
            var paint = paintProvider(border, lineThickness);
            if (Math.Abs(x1 - x2) < 0.01f)
            {
                canvas.DrawLine(x1 - offset, y1, x2 - offset, y2, paint);
                canvas.DrawLine(x1 + offset, y1, x2 + offset, y2, paint);
            }
            else
            {
                canvas.DrawLine(x1, y1 - offset, x2, y2 - offset, paint);
                canvas.DrawLine(x1, y1 + offset, x2, y2 + offset, paint);
            }

            return;
        }

        var singlePaint = paintProvider(border, thickness);
        canvas.DrawLine(x1, y1, x2, y2, singlePaint);
    }

    private static float GetBorderThickness(BorderLine border)
    {
        var thickness = MathF.Max(0f, border.Thickness);
        if (border.Style == DocBorderStyle.Hairline)
        {
            return MathF.Max(0.5f, MathF.Min(thickness, 0.5f));
        }

        if (border.Style == DocBorderStyle.Thick)
        {
            return MathF.Max(thickness, 2f);
        }

        return MathF.Max(0.5f, thickness);
    }

    private static float GetBorderWeight(BorderLine border)
    {
        var thickness = GetBorderThickness(border);
        return border.Style == DocBorderStyle.Double ? thickness * 2f : thickness;
    }

    private static SKPathEffect? CreateBorderEffect(DocBorderStyle style, float thickness)
    {
        var unit = MathF.Max(1f, thickness);
        return style switch
        {
            DocBorderStyle.Dotted => SKPathEffect.CreateDash(new[] { unit, unit }, 0),
            DocBorderStyle.Dashed => SKPathEffect.CreateDash(new[] { unit * 4f, unit * 2f }, 0),
            DocBorderStyle.DotDash => SKPathEffect.CreateDash(new[] { unit, unit, unit * 4f, unit * 2f }, 0),
            DocBorderStyle.DotDotDash => SKPathEffect.CreateDash(new[] { unit, unit, unit, unit, unit * 4f, unit * 2f }, 0),
            _ => null
        };
    }

    private readonly record struct BorderPaintKey(DocColor Color, float Thickness, DocBorderStyle Style);

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

    private void DrawImage(SKCanvas canvas, LayoutImage image, float lineX, float baseline, float ascent)
    {
        var bitmap = GetBitmap(image.Image);
        if (bitmap is null)
        {
            return;
        }

        var x = lineX + image.X;
        var y = baseline - image.Height;
        var dest = new SKRect(x, y, x + image.Width, y + image.Height);
        canvas.DrawBitmap(bitmap, dest);
    }

    private SKBitmap? GetBitmap(ImageInline inline)
    {
        if (_invalidImages.Contains(inline.Id))
        {
            return null;
        }

        if (!_imageCache.TryGetValue(inline.Id, out var bitmap))
        {
            if (inline.Data is null || inline.Data.Length == 0)
            {
                _invalidImages.Add(inline.Id);
                return null;
            }

            try
            {
                bitmap = SKBitmap.Decode(inline.Data);
                if (bitmap is not null)
                {
                    _imageCache[inline.Id] = bitmap;
                }
            }
            catch
            {
                _invalidImages.Add(inline.Id);
                return null;
            }
        }

        return bitmap;
    }

    private static IEnumerable<LineSegment> EnumerateSegments(LayoutLine line)
    {
        return EnumerateSegments(line.Runs, line.Images);
    }

    private static IEnumerable<LineSegment> EnumerateSegments(IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images)
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
                segments.Add((run.X, LineSegment.CreateText(run.Text, run.Style)));
            }
        }

        foreach (var image in images)
        {
            segments.Add((image.X, LineSegment.Image(image.Width)));
        }

        foreach (var segment in segments.OrderBy(item => item.X))
        {
            yield return segment.Segment;
        }
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

        private LineSegment(string text, TextStyle style, float width, int length, bool isTab, bool isImage)
        {
            Text = text;
            Style = style;
            Width = width;
            Length = length;
            IsTab = isTab;
            IsImage = isImage;
        }

        public static LineSegment CreateText(string text, TextStyle style)
        {
            return new LineSegment(text, style, 0f, text.Length, false, false);
        }

        public static LineSegment Tab(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, true, false);
        }

        public static LineSegment Image(float width)
        {
            return new LineSegment(string.Empty, new TextStyle(), width, 1, false, true);
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

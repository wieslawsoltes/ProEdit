using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Office.Editing;

public sealed class EditorSelectionService
{
    private readonly Document _document;
    private readonly EditorLayoutService _layoutService;
    private readonly ITextMeasurer _measurer;
    private TextPosition _selectionAnchor;
    private readonly Dictionary<RunMetricsKey, RunMetrics> _runMetricsCache = new();

    public TextPosition Caret { get; private set; }
    public TextRange Selection { get; private set; }
    public Guid? SelectedFloatingObjectId { get; private set; }

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    public EditorSelectionService(Document document, EditorLayoutService layoutService, ITextMeasurer measurer)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
        _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
        Caret = new TextPosition(0, 0);
        _selectionAnchor = Caret;
        Selection = new TextRange(Caret, Caret);
    }

    public void MoveLeft(bool extendSelection)
    {
        if (!extendSelection && !Selection.IsEmpty)
        {
            SetCaret(Selection.Normalize().Start, false);
            return;
        }

        if (Caret.Offset > 0)
        {
            SetCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset - 1), extendSelection);
            return;
        }

        if (Caret.ParagraphIndex > 0)
        {
            var previous = _document.GetParagraph(Caret.ParagraphIndex - 1);
            SetCaret(new TextPosition(Caret.ParagraphIndex - 1, DocumentEditHelpers.GetParagraphLength(previous)), extendSelection);
        }
    }

    public void MoveRight(bool extendSelection)
    {
        if (!extendSelection && !Selection.IsEmpty)
        {
            SetCaret(Selection.Normalize().End, false);
            return;
        }

        var paragraph = _document.GetParagraph(Caret.ParagraphIndex);
        if (Caret.Offset < DocumentEditHelpers.GetParagraphLength(paragraph))
        {
            SetCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset + 1), extendSelection);
            return;
        }

        if (Caret.ParagraphIndex < _document.ParagraphCount - 1)
        {
            SetCaret(new TextPosition(Caret.ParagraphIndex + 1, 0), extendSelection);
        }
    }

    public void MoveUp(bool extendSelection)
    {
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        var currentIndex = FindLineIndexForCaret(out var currentLine);
        if (currentIndex <= 0)
        {
            SetCaret(new TextPosition(0, 0), extendSelection);
            return;
        }

        var targetLine = layout.Lines[currentIndex - 1];
        var caretPoint = GetCaretPoint(currentLine);
        var offset = GetOffsetFromLine(targetLine, caretPoint.X, caretPoint.Y);
        SetCaret(new TextPosition(targetLine.ParagraphIndex, offset), extendSelection);
    }

    public void MoveDown(bool extendSelection)
    {
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        var currentIndex = FindLineIndexForCaret(out var currentLine);
        if (currentIndex >= layout.Lines.Count - 1)
        {
            var lastParagraphIndex = _document.ParagraphCount - 1;
            var lastParagraph = _document.GetParagraph(lastParagraphIndex);
            SetCaret(new TextPosition(lastParagraphIndex, DocumentEditHelpers.GetParagraphLength(lastParagraph)), extendSelection);
            return;
        }

        var targetLine = layout.Lines[currentIndex + 1];
        var caretPoint = GetCaretPoint(currentLine);
        var offset = GetOffsetFromLine(targetLine, caretPoint.X, caretPoint.Y);
        SetCaret(new TextPosition(targetLine.ParagraphIndex, offset), extendSelection);
    }

    public void SetCaretFromPoint(float x, float y, bool extendSelection)
    {
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        if (TrySelectFloatingObject(x, y))
        {
            return;
        }

        ClearFloatingSelection();
        if (TrySetCaretFromTable(x, y, extendSelection))
        {
            return;
        }

        var lineIndex = FindLineIndexFromPoint(x, y, out var line);
        if (lineIndex < 0 || lineIndex >= layout.Lines.Count)
        {
            return;
        }

        if (line.ParagraphIndex < 0)
        {
            return;
        }

        var offset = GetOffsetFromLine(line, x, y);
        SetCaret(new TextPosition(line.ParagraphIndex, offset), extendSelection);
    }

    public bool TrySelectFloatingObject(Guid id)
    {
        var layout = _layoutService.Layout;
        if (layout.FloatingObjects.Count == 0)
        {
            return false;
        }

        for (var i = layout.FloatingObjects.Count - 1; i >= 0; i--)
        {
            var floating = layout.FloatingObjects[i];
            if (floating.Object.Id != id)
            {
                continue;
            }

            SetFloatingSelection(floating.Object.Id, floating.PageIndex);
            return true;
        }

        return false;
    }

    public bool TrySelectFirstFloatingObject()
    {
        var layout = _layoutService.Layout;
        if (layout.FloatingObjects.Count == 0)
        {
            return false;
        }

        var floating = layout.FloatingObjects[^1];
        SetFloatingSelection(floating.Object.Id, floating.PageIndex);
        return true;
    }

    public void SetCaret(TextPosition position, bool extendSelection)
    {
        MoveCaret(position, extendSelection);
    }

    public EquationInline? GetEquationAtCaret()
    {
        return GetEquationAtPosition(Caret);
    }

    public EquationInline? GetEquationAtPosition(TextPosition position)
    {
        if (position.ParagraphIndex < 0 || position.ParagraphIndex >= _document.ParagraphCount)
        {
            return null;
        }

        var paragraph = _document.GetParagraph(position.ParagraphIndex);
        return DocumentEditHelpers.FindEquationInline(paragraph, position.Offset);
    }

    private bool TrySelectFloatingObject(float x, float y)
    {
        var layout = _layoutService.Layout;
        if (layout.FloatingObjects.Count == 0)
        {
            return false;
        }

        for (var i = layout.FloatingObjects.Count - 1; i >= 0; i--)
        {
            var floating = layout.FloatingObjects[i];
            if (!floating.Bounds.Contains(x, y))
            {
                continue;
            }

            SetFloatingSelection(floating.Object.Id, floating.PageIndex);
            return true;
        }

        return false;
    }

    private void SetFloatingSelection(Guid id, int pageIndex)
    {
        if (SelectedFloatingObjectId == id)
        {
            return;
        }

        var previousSelection = Selection;
        SelectedFloatingObjectId = id;
        Selection = new TextRange(Caret, Caret);

        var dirty = new HashSet<int>(_layoutService.ComputeDirtyPagesForSelection(previousSelection, Selection));
        if (pageIndex >= 0)
        {
            dirty.Add(pageIndex);
        }

        RaiseSelectionChanged(dirty);
    }

    private void ClearFloatingSelection()
    {
        SelectedFloatingObjectId = null;
    }

    private bool TrySetCaretFromTable(float x, float y, bool extendSelection)
    {
        var layout = _layoutService.Layout;
        foreach (var table in layout.Tables)
        {
            if (!table.Bounds.Contains(x, y))
            {
                continue;
            }

            foreach (var cell in table.Cells)
            {
                if (!cell.Bounds.Contains(x, y))
                {
                    continue;
                }

                var line = FindTableLineAtPoint(cell.Lines, x, y);
                if (line is null)
                {
                    return false;
                }

                var offset = GetOffsetFromLine(line, x, y);
                SetCaret(new TextPosition(line.ParagraphIndex, offset), extendSelection);
                return true;
            }
        }

        return false;
    }

    private static TableCellLine? FindTableLineAtPoint(IReadOnlyList<TableCellLine> lines, float x, float y)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        foreach (var line in lines)
        {
            if (DocTextDirectionHelpers.IsVertical(line.TextDirection))
            {
                if (IsPointWithinLine(line.X, line.Y, line.TextDirection, line.Width, line.LineHeight, x, y))
                {
                    return line;
                }

                continue;
            }

            if (y >= line.Y && y <= line.Y + line.LineHeight)
            {
                return line;
            }
        }

        return y < lines[0].Y ? lines[0] : lines[^1];
    }

    private int GetOffsetFromLine(TableCellLine line, float x, float y)
    {
        var localX = GetLineLocalX(line.X, line.Y, line.TextDirection, x, y);
        if (localX <= 0)
        {
            return line.StartOffset;
        }

        if (line.Length == 0)
        {
            return line.StartOffset;
        }

        var segments = BuildVisualSegments(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
        if (segments.Count == 0)
        {
            return line.StartOffset;
        }

        var totalWidth = segments[^1].X + segments[^1].Width;
        if (localX >= totalWidth)
        {
            return line.StartOffset + line.Length;
        }

        foreach (var segment in segments)
        {
            var segmentEndX = segment.X + segment.Width;
            if (localX > segmentEndX)
            {
                continue;
            }

            var segmentLocalX = localX - segment.X;
            if (segment.IsRtl)
            {
                segmentLocalX = segment.Width - segmentLocalX;
            }

            var offsetInSegment = GetOffsetForSegmentX(segment, segmentLocalX);
            return line.StartOffset + segment.StartOffset + offsetInSegment;
        }

        return line.StartOffset + line.Length;
    }

    private void MoveCaret(TextPosition position, bool extendSelection)
    {
        var previousSelection = Selection;
        var previousFloating = SelectedFloatingObjectId;
        var clamped = ClampPosition(position);
        if (previousFloating.HasValue)
        {
            SelectedFloatingObjectId = null;
        }

        Caret = clamped;
        if (!extendSelection)
        {
            _selectionAnchor = clamped;
        }

        Selection = new TextRange(_selectionAnchor, clamped);
        var dirty = new HashSet<int>(_layoutService.ComputeDirtyPagesForSelection(previousSelection, Selection));
        if (previousFloating.HasValue)
        {
            var pageIndex = ResolveFloatingPageIndex(previousFloating.Value);
            if (pageIndex.HasValue)
            {
                dirty.Add(pageIndex.Value);
            }
        }

        RaiseSelectionChanged(dirty);
    }

    private int? ResolveFloatingPageIndex(Guid id)
    {
        var layout = _layoutService.Layout;
        foreach (var floating in layout.FloatingObjects)
        {
            if (floating.Object.Id == id)
            {
                return floating.PageIndex;
            }
        }

        return null;
    }

    private TextPosition ClampPosition(TextPosition position)
    {
        if (_document.ParagraphCount == 0)
        {
            _document.Blocks.Add(new ParagraphBlock());
        }

        var paragraphIndex = Math.Clamp(position.ParagraphIndex, 0, _document.ParagraphCount - 1);
        var paragraph = _document.GetParagraph(paragraphIndex);
        var offset = Math.Clamp(position.Offset, 0, DocumentEditHelpers.GetParagraphLength(paragraph));
        return new TextPosition(paragraphIndex, offset);
    }

    private int FindLineIndexForCaret(out LayoutLine line)
    {
        return FindLineIndexForPosition(_layoutService.Layout, Caret, out line);
    }

    private (float X, float Y) GetCaretPoint(LayoutLine line)
    {
        var offsetInLine = Math.Clamp(Caret.Offset - line.StartOffset, 0, line.Length);
        var localX = MeasureLineOffset(line, offsetInLine);
        return GetLineWorldPoint(line.X, line.Y, line.TextDirection, localX, 0f);
    }

    public static int FindLineIndexForPosition(DocumentLayout layout, TextPosition position, out LayoutLine line)
    {
        if (layout.Lines.Count == 0)
        {
            line = default!;
            return 0;
        }

        line = layout.Lines[0];
        if (!layout.ParagraphLineRanges.TryGetValue(position.ParagraphIndex, out var range) || range.Count == 0)
        {
            return 0;
        }

        var start = range.Start;
        var end = Math.Min(layout.Lines.Count, range.End);
        var lastIndexInParagraph = Math.Clamp(end - 1, 0, layout.Lines.Count - 1);

        for (var i = start; i < end; i++)
        {
            var candidate = layout.Lines[i];
            if (candidate.ParagraphIndex != position.ParagraphIndex)
            {
                continue;
            }

            lastIndexInParagraph = i;
            var lineStart = candidate.StartOffset;
            var lineEnd = candidate.StartOffset + candidate.Length;
            if (position.Offset >= lineStart && position.Offset <= lineEnd)
            {
                line = candidate;
                return i;
            }
        }

        line = layout.Lines[lastIndexInParagraph];
        return lastIndexInParagraph;
    }

    private int FindLineIndexFromPoint(float x, float y, out LayoutLine line)
    {
        var layout = _layoutService.Layout;
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            var candidate = layout.Lines[i];
            if (!DocTextDirectionHelpers.IsVertical(candidate.TextDirection))
            {
                continue;
            }

            if (IsPointWithinLine(candidate.X, candidate.Y, candidate.TextDirection, candidate.Width, candidate.LineHeight, x, y))
            {
                line = candidate;
                return i;
            }
        }

        var index = layout.LineIndex.FindLineAtY(y);
        if (index >= 0 && index < layout.Lines.Count)
        {
            line = layout.Lines[index];
            return index;
        }

        line = default!;
        return -1;
    }

    private static float GetLineLocalX(float originX, float originY, DocTextDirection? direction, float x, float y)
    {
        var (localX, _) = GetLineLocalPoint(originX, originY, direction, x, y);
        return localX;
    }

    private static (float X, float Y) GetLineLocalPoint(float originX, float originY, DocTextDirection? direction, float x, float y)
    {
        var dx = x - originX;
        var dy = y - originY;
        if (!DocTextDirectionHelpers.IsVertical(direction))
        {
            return (dx, dy);
        }

        var radians = DegreesToRadians(DocTextDirectionHelpers.GetRotationDegrees(direction!.Value));
        var cos = MathF.Cos(-radians);
        var sin = MathF.Sin(-radians);
        return (dx * cos - dy * sin, dx * sin + dy * cos);
    }

    private static (float X, float Y) GetLineWorldPoint(float originX, float originY, DocTextDirection? direction, float localX, float localY)
    {
        if (!DocTextDirectionHelpers.IsVertical(direction))
        {
            return (originX + localX, originY + localY);
        }

        var radians = DegreesToRadians(DocTextDirectionHelpers.GetRotationDegrees(direction!.Value));
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        var worldX = originX + localX * cos - localY * sin;
        var worldY = originY + localX * sin + localY * cos;
        return (worldX, worldY);
    }

    private static bool IsPointWithinLine(
        float originX,
        float originY,
        DocTextDirection? direction,
        float lineWidth,
        float lineHeight,
        float x,
        float y)
    {
        var (localX, localY) = GetLineLocalPoint(originX, originY, direction, x, y);
        return localX >= 0f && localX <= lineWidth && localY >= 0f && localY <= lineHeight;
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180f);
    }

    private int GetOffsetFromLine(LayoutLine line, float x, float y)
    {
        var localX = GetLineLocalX(line.X, line.Y, line.TextDirection, x, y);
        return GetOffsetFromLineLocal(line, localX);
    }

    private int GetOffsetFromLineLocal(LayoutLine line, float localX)
    {
        if (localX <= 0)
        {
            return line.StartOffset;
        }

        if (line.Length == 0)
        {
            return line.StartOffset;
        }

        var segments = BuildVisualSegments(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
        if (segments.Count == 0)
        {
            return line.StartOffset;
        }

        var totalWidth = segments[^1].X + segments[^1].Width;
        if (localX >= totalWidth)
        {
            return line.StartOffset + line.Length;
        }

        foreach (var segment in segments)
        {
            var segmentEndX = segment.X + segment.Width;
            if (localX > segmentEndX)
            {
                continue;
            }

            var segmentLocalX = localX - segment.X;
            if (segment.IsRtl)
            {
                segmentLocalX = segment.Width - segmentLocalX;
            }

            var offsetInSegment = GetOffsetForSegmentX(segment, segmentLocalX);
            return line.StartOffset + segment.StartOffset + offsetInSegment;
        }

        return line.StartOffset + line.Length;
    }

    private float MeasureLineOffset(LayoutLine line, int length)
    {
        if (length <= 0)
        {
            return 0f;
        }

        var segments = BuildVisualSegments(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
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
            var localX = MeasureSegmentOffset(containing, offsetInSegment);
            return containing.X + localX;
        }

        return target <= 0 ? 0f : totalWidth;
    }

    private List<VisualSegment> BuildVisualSegments(
        ReadOnlySpan<char> lineText,
        bool baseRtl,
        IReadOnlyList<LayoutRun> runs,
        IReadOnlyList<LayoutImage> images,
        IReadOnlyList<LayoutShape> shapes,
        IReadOnlyList<LayoutChart> charts,
        IReadOnlyList<LayoutEquation> equations)
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
                        var metricsWidth = MeasureRunSegmentWidth(run, runStart, overlapLength, out var scale);
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

    private float MeasureRunSegmentWidth(LayoutRun run, int segmentStart, int segmentLength, out float scale)
    {
        scale = 1f;
        if (string.IsNullOrEmpty(run.Text) || segmentLength <= 0)
        {
            return 0f;
        }

        var metrics = GetRunMetrics(run.Text, run.Style, run.LetterSpacing);
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

    private float MeasureSegmentOffset(VisualSegment segment, int offsetInSegment)
    {
        if (offsetInSegment <= 0)
        {
            return segment.IsRtl ? segment.Width : 0f;
        }

        float width;
        if (segment.IsText && segment.Run is not null)
        {
            var run = segment.Run;
            var metrics = GetRunMetrics(run.Text, run.Style, run.LetterSpacing);
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

    private int GetOffsetForSegmentX(VisualSegment segment, float localX)
    {
        if (segment.Length <= 0)
        {
            return 0;
        }

        if (!segment.IsText || segment.Run is null)
        {
            return localX >= segment.Width / 2f ? segment.Length : 0;
        }

        var run = segment.Run;
        var metrics = GetRunMetrics(run.Text, run.Style, run.LetterSpacing);
        var startWidth = metrics.GetWidth(segment.RunStart);
        var adjustedX = segment.Scale != 1f && segment.Scale != 0f
            ? startWidth + localX / segment.Scale
            : startWidth + localX;
        var offsetInRun = metrics.GetOffsetForX(adjustedX);
        var offsetInSegment = offsetInRun - segment.RunStart;
        return Math.Clamp(offsetInSegment, 0, segment.Length);
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

    private float GetSegmentWidth(LineSegment segment)
    {
        if (segment.IsImage || segment.IsShape || segment.IsChart || segment.IsEquation || segment.IsTab)
        {
            return segment.Width;
        }

        var metrics = GetRunMetrics(segment.Text, segment.Style, segment.LetterSpacing);
        if (segment.Width > 0f)
        {
            if (segment.Length == 1)
            {
                return segment.Width;
            }

            if (MathF.Abs(segment.Width - metrics.Width) > 0.01f)
            {
                return segment.Width;
            }
        }

        return metrics.Width;
    }

    private RunMetrics GetRunMetrics(string text, TextStyle style, float letterSpacing)
    {
        if (string.IsNullOrEmpty(text))
        {
            return RunMetrics.Empty;
        }

        var key = new RunMetricsKey(text, style, letterSpacing);
        if (_runMetricsCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var shapeInfo = BuildShapeInfo(text, style);
        var metrics = new RunMetrics(shapeInfo, letterSpacing);
        _runMetricsCache[key] = metrics;
        return metrics;
    }

    private TextShapeInfo BuildShapeInfo(string text, TextStyle style)
    {
        if (_measurer is ITextMeasurerAdvanced advanced)
        {
            var shaped = advanced.ShapeText(text, style);
            if (shaped.ClusterOffsets.Length == shaped.ClusterAdvances.Length)
            {
                return shaped;
            }
        }

        if (text.Length == 0)
        {
            return new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>());
        }

        var offsets = new int[text.Length];
        var advances = new float[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            offsets[i] = i;
            advances[i] = _measurer.MeasureText(text[i].ToString(), style).Width;
        }

        return new TextShapeInfo(text.Length, offsets, advances);
    }

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

        public RunMetricsKey(string text, TextStyle style, float letterSpacing)
        {
            _text = text;
            _styleKey = new TextStyleKey(style);
            _letterSpacing = letterSpacing;
        }

        public bool Equals(RunMetricsKey other)
        {
            return _text == other._text && _styleKey.Equals(other._styleKey) && _letterSpacing.Equals(other._letterSpacing);
        }

        public override bool Equals(object? obj) => obj is RunMetricsKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_text, _styleKey, _letterSpacing);
    }

    private sealed class RunMetrics
    {
        public static readonly RunMetrics Empty = new RunMetrics(new TextShapeInfo(0, Array.Empty<int>(), Array.Empty<float>()), 0f);
        private readonly int _textLength;
        private readonly int[] _clusterOffsets;
        private readonly float[] _clusterPositions;
        private readonly float _totalWidth;

        public RunMetrics(TextShapeInfo shape, float letterSpacing)
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

                total += advance;
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

        public int GetOffsetForX(float x)
        {
            if (_clusterOffsets.Length == 0 || x <= 0f)
            {
                return 0;
            }

            if (x >= Width)
            {
                return _textLength;
            }

            var index = GetClusterIndexForX(x);
            var currentOffset = _clusterOffsets[index];
            var currentX = _clusterPositions[index];
            var nextIndex = index + 1;
            var nextOffset = nextIndex < _clusterOffsets.Length ? _clusterOffsets[nextIndex] : _textLength;
            var nextX = nextIndex < _clusterPositions.Length ? _clusterPositions[nextIndex] : _totalWidth;
            var midpoint = currentX + (nextX - currentX) / 2f;
            return x >= midpoint ? nextOffset : currentOffset;
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

        private int GetClusterIndexForX(float x)
        {
            if (_clusterPositions.Length == 0)
            {
                return 0;
            }

            var low = 0;
            var high = _clusterPositions.Length - 1;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (_clusterPositions[mid] <= x)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return _clusterPositions[low] <= x ? low : Math.Max(0, low - 1);
        }
    }

    private void RaiseSelectionChanged(HashSet<int> dirtyPages)
    {
        var pages = dirtyPages.Count == 0 ? Array.Empty<int>() : dirtyPages.ToArray();
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(pages));
    }
}

public sealed class SelectionChangedEventArgs : EventArgs
{
    public IReadOnlyList<int> DirtyPages { get; }

    public SelectionChangedEventArgs(IReadOnlyList<int> dirtyPages)
    {
        DirtyPages = dirtyPages;
    }
}

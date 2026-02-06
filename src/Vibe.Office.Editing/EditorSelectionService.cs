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
    private DocumentLayout? _cachedLayout;
    private int[][] _tablesByPage = Array.Empty<int[]>();
    private int[] _verticalLineIndices = Array.Empty<int>();
    private readonly List<TextRange> _selectionRanges = new();
    private readonly List<TableSelectionRange> _tableSelections = new();
    private readonly List<Guid> _selectedFloatingObjectIds = new();

    public TextPosition Caret { get; private set; }
    public TextRange Selection { get; private set; }
    public IReadOnlyList<TextRange> SelectionRanges => _selectionRanges;
    public IReadOnlyList<TableSelectionRange> TableSelections => _tableSelections;
    public Guid? SelectedFloatingObjectId => _selectedFloatingObjectIds.Count > 0 ? _selectedFloatingObjectIds[0] : null;
    public IReadOnlyList<Guid> SelectedFloatingObjectIds => _selectedFloatingObjectIds;

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    public EditorSelectionService(Document document, EditorLayoutService layoutService, ITextMeasurer measurer)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
        _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
        Caret = new TextPosition(0, 0);
        _selectionAnchor = Caret;
        Selection = new TextRange(Caret, Caret);
        _selectionRanges.Add(Selection);
        UpdateTableSelections();
    }

    public void MoveLeft(bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        if (!extendSelection && !Selection.IsEmpty)
        {
            SetCaret(Selection.Normalize().Start, SelectionUpdateMode.Replace);
            return;
        }

        if (TryMoveCaretHorizontally(-1, mode))
        {
            return;
        }

        var layout = _layoutService.Layout;
        if (Caret.Offset > 0)
        {
            SetCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset - 1), mode);
            return;
        }

        if (Caret.ParagraphIndex > 0)
        {
            var previous = GetParagraphAt(layout, Caret.ParagraphIndex - 1);
            SetCaret(new TextPosition(Caret.ParagraphIndex - 1, DocumentEditHelpers.GetParagraphLength(previous)), mode);
        }
    }

    public void MoveRight(bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        if (!extendSelection && !Selection.IsEmpty)
        {
            SetCaret(Selection.Normalize().End, SelectionUpdateMode.Replace);
            return;
        }

        if (TryMoveCaretHorizontally(1, mode))
        {
            return;
        }

        var layout = _layoutService.Layout;
        var paragraph = GetParagraphAt(layout, Caret.ParagraphIndex);
        if (Caret.Offset < DocumentEditHelpers.GetParagraphLength(paragraph))
        {
            SetCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset + 1), mode);
            return;
        }

        if (Caret.ParagraphIndex < GetParagraphCount(layout) - 1)
        {
            SetCaret(new TextPosition(Caret.ParagraphIndex + 1, 0), mode);
        }
    }

    public void MoveUp(bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        var currentIndex = FindLineIndexForCaret(out var currentLine);
        if (currentIndex <= 0)
        {
            SetCaret(new TextPosition(0, 0), mode);
            return;
        }

        var targetLine = layout.Lines[currentIndex - 1];
        var caretPoint = GetCaretPoint(currentLine);
        var offset = GetOffsetFromLine(targetLine, caretPoint.X, caretPoint.Y);
        SetCaret(new TextPosition(targetLine.ParagraphIndex, offset), mode);
    }

    public void MoveDown(bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        var currentIndex = FindLineIndexForCaret(out var currentLine);
        if (currentIndex >= layout.Lines.Count - 1)
        {
            var lastParagraphIndex = GetParagraphCount(layout) - 1;
            var lastParagraph = GetParagraphAt(layout, lastParagraphIndex);
            SetCaret(new TextPosition(lastParagraphIndex, DocumentEditHelpers.GetParagraphLength(lastParagraph)), mode);
            return;
        }

        var targetLine = layout.Lines[currentIndex + 1];
        var caretPoint = GetCaretPoint(currentLine);
        var offset = GetOffsetFromLine(targetLine, caretPoint.X, caretPoint.Y);
        SetCaret(new TextPosition(targetLine.ParagraphIndex, offset), mode);
    }

    public void MoveLineStart(bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        var lineIndex = FindLineIndexForCaret(out var line);
        if (lineIndex < 0 || lineIndex >= layout.Lines.Count)
        {
            SetCaret(new TextPosition(0, 0), mode);
            return;
        }

        SetCaret(new TextPosition(line.ParagraphIndex, line.StartOffset), mode);
    }

    public void MoveLineEnd(bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        var lineIndex = FindLineIndexForCaret(out var line);
        if (lineIndex < 0 || lineIndex >= layout.Lines.Count)
        {
            return;
        }

        SetCaret(new TextPosition(line.ParagraphIndex, line.StartOffset + line.Length), mode);
    }

    public void MoveDocumentStart(bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        SetCaret(new TextPosition(0, 0), mode);
    }

    public void MoveDocumentEnd(bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        var layout = _layoutService.Layout;
        var paragraphCount = GetParagraphCount(layout);
        if (paragraphCount <= 0)
        {
            return;
        }

        var lastIndex = paragraphCount - 1;
        var lastParagraph = GetParagraphAt(layout, lastIndex);
        var endOffset = DocumentEditHelpers.GetParagraphLength(lastParagraph);
        SetCaret(new TextPosition(lastIndex, endOffset), mode);
    }

    public void MovePageUp(bool extendSelection)
    {
        MovePage(false, extendSelection);
    }

    public void MovePageDown(bool extendSelection)
    {
        MovePage(true, extendSelection);
    }

    public void SelectAll()
    {
        var layout = _layoutService.Layout;
        var paragraphCount = GetParagraphCount(layout);
        if (paragraphCount <= 0)
        {
            return;
        }

        var lastIndex = paragraphCount - 1;
        var lastParagraph = GetParagraphAt(layout, lastIndex);
        var endOffset = DocumentEditHelpers.GetParagraphLength(lastParagraph);
        SetSelection(new TextRange(new TextPosition(0, 0), new TextPosition(lastIndex, endOffset)), SelectionUpdateMode.Replace);
    }

    private void MovePage(bool down, bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        if (!TryGetCaretPoint(out var caretPoint, out _))
        {
            if (down)
            {
                MoveDocumentEnd(extendSelection);
            }
            else
            {
                MoveDocumentStart(extendSelection);
            }

            return;
        }

        var targetX = caretPoint.X;
        var targetY = caretPoint.Y;
        var pageIndex = FindPageIndexForPoint(layout.Pages, caretPoint.X, caretPoint.Y, layout.Settings.PageFlow);
        if (pageIndex < 0 || pageIndex >= layout.Pages.Count)
        {
            pageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, layout.Pages.Count - 1));
        }

        if (layout.Pages.Count > 0 && pageIndex >= 0 && pageIndex < layout.Pages.Count)
        {
            var pageBounds = layout.Pages[pageIndex].Bounds;
            var delta = layout.Settings.PageFlow == PageFlowDirection.Horizontal
                ? Math.Max(1f, pageBounds.Width)
                : Math.Max(1f, pageBounds.Height);
            if (layout.Settings.PageFlow == PageFlowDirection.Horizontal)
            {
                targetX += down ? delta : -delta;
            }
            else
            {
                targetY += down ? delta : -delta;
            }
        }

        var targetLineIndex = FindLineIndexFromPoint(targetX, targetY, out var targetLine);
        if (targetLineIndex < 0 || targetLineIndex >= layout.Lines.Count)
        {
            if (down)
            {
                MoveDocumentEnd(extendSelection);
            }
            else
            {
                MoveDocumentStart(extendSelection);
            }

            return;
        }

        var targetOffset = GetOffsetFromLine(targetLine, targetX, targetY);
        SetCaret(new TextPosition(targetLine.ParagraphIndex, targetOffset), mode);
    }

    private bool TryMoveCaretHorizontally(int direction, SelectionUpdateMode mode)
    {
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        var lineIndex = FindLineIndexForCaret(out var line);
        if (lineIndex < 0 || lineIndex >= layout.Lines.Count)
        {
            return false;
        }

        var lineEndOffset = line.StartOffset + line.Length;
        if (Caret.Offset == lineEndOffset && lineIndex + 1 < layout.Lines.Count)
        {
            var nextLine = layout.Lines[lineIndex + 1];
            if (nextLine.ParagraphIndex == line.ParagraphIndex && nextLine.StartOffset == lineEndOffset)
            {
                lineIndex++;
                line = nextLine;
            }
        }

        var stops = BuildVisualCaretStops(line);
        if (stops.Count == 0)
        {
            return false;
        }

        var currentOffset = Caret.Offset;
        var stopIndex = FindCaretStopIndex(stops, currentOffset);
        if (stopIndex < 0)
        {
            var offsetInLine = Math.Clamp(currentOffset - line.StartOffset, 0, line.Length);
            var localX = MeasureLineOffset(line, offsetInLine);
            stopIndex = FindNearestCaretStopIndex(stops, localX);
        }

        var nextIndex = stopIndex + direction;
        if (nextIndex >= 0 && nextIndex < stops.Count)
        {
            var targetOffset = stops[nextIndex].Offset;
            SetCaret(new TextPosition(line.ParagraphIndex, targetOffset), mode);
            return true;
        }

        return TryMoveCaretToAdjacentLine(layout, lineIndex, direction, currentOffset, mode);
    }

    private bool TryMoveCaretToAdjacentLine(
        DocumentLayout layout,
        int lineIndex,
        int direction,
        int currentOffset,
        SelectionUpdateMode mode)
    {
        var targetIndex = lineIndex + direction;
        if (targetIndex < 0 || targetIndex >= layout.Lines.Count)
        {
            return false;
        }

        var targetLine = layout.Lines[targetIndex];
        var stops = BuildVisualCaretStops(targetLine);
        if (stops.Count == 0)
        {
            return false;
        }

        if (direction < 0)
        {
            for (var i = stops.Count - 1; i >= 0; i--)
            {
                if (stops[i].Offset == currentOffset)
                {
                    continue;
                }

                SetCaret(new TextPosition(targetLine.ParagraphIndex, stops[i].Offset), mode);
                return true;
            }
        }
        else
        {
            for (var i = 0; i < stops.Count; i++)
            {
                if (stops[i].Offset == currentOffset)
                {
                    continue;
                }

                SetCaret(new TextPosition(targetLine.ParagraphIndex, stops[i].Offset), mode);
                return true;
            }
        }

        return false;
    }

    public void SetCaretFromPoint(float x, float y, bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        SetCaretFromPoint(x, y, mode);
    }

    public void SetCaretFromPoint(float x, float y, SelectionUpdateMode mode)
    {
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return;
        }

        EnsureLayoutCaches(layout);

        if (TrySelectFloatingObject(x, y, mode))
        {
            return;
        }

        ClearFloatingSelection();
        if (TrySetCaretFromTable(x, y, mode))
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
        SetCaret(new TextPosition(line.ParagraphIndex, offset), mode);
    }

    public bool TrySelectWordFromPoint(float x, float y, SelectionUpdateMode mode)
    {
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        SetCaretFromPoint(x, y, SelectionUpdateMode.Replace);
        if (SelectedFloatingObjectId is not null)
        {
            return true;
        }

        if (Caret.ParagraphIndex < 0 || Caret.ParagraphIndex >= GetParagraphCount(layout))
        {
            return true;
        }

        var paragraph = GetParagraphAt(layout, Caret.ParagraphIndex);
        var text = DocumentEditHelpers.GetParagraphText(paragraph);
        if (!TryGetWordSpanAtOffset(text.AsSpan(), Caret.Offset, out var start, out var length))
        {
            return true;
        }

        var startPosition = new TextPosition(Caret.ParagraphIndex, start);
        var endPosition = new TextPosition(Caret.ParagraphIndex, start + length);
        SetSelection(new TextRange(startPosition, endPosition), mode);
        return true;
    }

    public bool TrySelectParagraphFromPoint(float x, float y, SelectionUpdateMode mode)
    {
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        SetCaretFromPoint(x, y, SelectionUpdateMode.Replace);
        if (SelectedFloatingObjectId is not null)
        {
            return true;
        }

        if (Caret.ParagraphIndex < 0 || Caret.ParagraphIndex >= GetParagraphCount(layout))
        {
            return true;
        }

        var paragraph = GetParagraphAt(layout, Caret.ParagraphIndex);
        var length = DocumentEditHelpers.GetParagraphLength(paragraph);
        var startPosition = new TextPosition(Caret.ParagraphIndex, 0);
        var endPosition = new TextPosition(Caret.ParagraphIndex, length);
        SetSelection(new TextRange(startPosition, endPosition), mode);
        return true;
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

            SetFloatingSelection(floating.Object.Id, floating.PageIndex, SelectionUpdateMode.Replace);
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
        SetFloatingSelection(floating.Object.Id, floating.PageIndex, SelectionUpdateMode.Replace);
        return true;
    }

    public bool TryGetCaretPoint(out DocPoint point, out int lineIndex)
    {
        point = default;
        lineIndex = -1;
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        lineIndex = FindLineIndexForCaret(out var line);
        if (lineIndex < 0 || lineIndex >= layout.Lines.Count)
        {
            lineIndex = -1;
            return false;
        }

        var caretPoint = GetCaretPoint(line);
        point = new DocPoint(caretPoint.X, caretPoint.Y);
        return true;
    }

    /// <summary>
    /// Attempts to resolve a caret point for an arbitrary position without mutating selection state.
    /// </summary>
    public bool TryGetCaretPoint(TextPosition position, out DocPoint point, out int lineIndex)
    {
        point = default;
        lineIndex = -1;
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        var clamped = ClampPosition(position);
        lineIndex = FindLineIndexForPosition(layout, clamped, out var line);
        if (lineIndex < 0 || lineIndex >= layout.Lines.Count)
        {
            lineIndex = -1;
            return false;
        }

        var offsetInLine = Math.Clamp(clamped.Offset - line.StartOffset, 0, line.Length);
        var localX = MeasureLineOffset(line, offsetInLine);
        var (worldX, worldY) = GetLineWorldPoint(line.X, line.Y, line.TextDirection, localX, 0f);
        point = new DocPoint(worldX, worldY);
        return true;
    }

    public void SetCaret(TextPosition position, bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        MoveCaret(position, mode);
    }

    public void SetCaret(TextPosition position, SelectionUpdateMode mode)
    {
        MoveCaret(position, mode);
    }

    public void SetSelection(TextRange selection, SelectionUpdateMode mode)
    {
        var previousRanges = CaptureSelectionRanges();
        var previousFloating = CaptureFloatingSelection();
        var add = mode.HasFlag(SelectionUpdateMode.Add);
        var extend = mode.HasFlag(SelectionUpdateMode.Extend);

        if (!add && !extend)
        {
            _selectionRanges.Clear();
        }

        if (_selectedFloatingObjectIds.Count > 0)
        {
            _selectedFloatingObjectIds.Clear();
        }

        _selectionAnchor = selection.Start;
        Selection = selection;
        Caret = selection.End;

        if (add && !extend)
        {
            _selectionRanges.Insert(0, Selection);
        }
        else if (_selectionRanges.Count == 0)
        {
            _selectionRanges.Add(Selection);
        }
        else
        {
            _selectionRanges[0] = Selection;
        }

        UpdateTableSelections();

        var dirty = new HashSet<int>();
        AddDirtyPagesForRanges(dirty, previousRanges);
        AddDirtyPagesForRanges(dirty, _selectionRanges);
        AddDirtyPagesForFloatingSelection(dirty, previousFloating);
        RaiseSelectionChanged(dirty);
    }

    public EquationInline? GetEquationAtCaret()
    {
        return GetEquationAtPosition(Caret);
    }

    public EquationInline? GetEquationAtPosition(TextPosition position)
    {
        var layout = _layoutService.Layout;
        if (position.ParagraphIndex < 0 || position.ParagraphIndex >= GetParagraphCount(layout))
        {
            return null;
        }

        var paragraph = GetParagraphAt(layout, position.ParagraphIndex);
        return DocumentEditHelpers.FindEquationInline(paragraph, position.Offset);
    }

    public bool TryGetContentControlAtCaret(out ContentControlHit hit)
    {
        return TryGetContentControlAtPosition(Caret, out hit);
    }

    public bool TryGetContentControlAtPosition(TextPosition position, out ContentControlHit hit)
    {
        hit = default;
        var layout = _layoutService.Layout;
        if (position.ParagraphIndex < 0 || position.ParagraphIndex >= GetParagraphCount(layout))
        {
            return false;
        }

        var paragraph = GetParagraphAt(layout, position.ParagraphIndex);
        if (!DocumentEditHelpers.TryFindContentControlAtOffset(paragraph, position.Offset, out var properties))
        {
            return false;
        }

        hit = new ContentControlHit(properties, position);
        return true;
    }

    public bool TryGetContentControlAtPoint(float x, float y, out ContentControlHit hit)
    {
        hit = default;
        var layout = _layoutService.Layout;
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        EnsureLayoutCaches(layout);
        var lineIndex = FindLineIndexFromPoint(x, y, out var line);
        if (lineIndex < 0 || line.ParagraphIndex < 0)
        {
            return false;
        }

        var offset = GetOffsetFromLine(line, x, y);
        var layoutOffset = line.StartOffset + offset;
        var paragraph = GetParagraphAt(layout, line.ParagraphIndex);
        if (!DocumentEditHelpers.TryFindContentControlAtLayoutOffset(_document, paragraph, layoutOffset, out var properties, out var documentOffset))
        {
            return false;
        }

        var clampedOffset = Math.Clamp(documentOffset, 0, DocumentEditHelpers.GetParagraphLength(paragraph));
        hit = new ContentControlHit(properties, new TextPosition(line.ParagraphIndex, clampedOffset));
        return true;
    }

    private bool TrySelectFloatingObject(float x, float y, SelectionUpdateMode mode)
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

            SetFloatingSelection(floating.Object.Id, floating.PageIndex, mode);
            return true;
        }

        return false;
    }

    private void SetFloatingSelection(Guid id, int pageIndex, SelectionUpdateMode mode)
    {
        var previousRanges = CaptureSelectionRanges();
        var previousFloating = CaptureFloatingSelection();
        var add = mode.HasFlag(SelectionUpdateMode.Add);

        if (!add)
        {
            _selectedFloatingObjectIds.Clear();
        }

        if (!_selectedFloatingObjectIds.Contains(id))
        {
            _selectedFloatingObjectIds.Insert(0, id);
        }

        _selectionAnchor = Caret;
        Selection = new TextRange(Caret, Caret);
        _selectionRanges.Clear();
        _selectionRanges.Add(Selection);

        UpdateTableSelections();

        var dirty = new HashSet<int>();
        AddDirtyPagesForRanges(dirty, previousRanges);
        AddDirtyPagesForRanges(dirty, _selectionRanges);
        AddDirtyPagesForFloatingSelection(dirty, previousFloating);
        AddDirtyPagesForFloatingSelection(dirty, _selectedFloatingObjectIds);
        if (pageIndex >= 0)
        {
            dirty.Add(pageIndex);
        }

        RaiseSelectionChanged(dirty);
    }

    private void ClearFloatingSelection()
    {
        _selectedFloatingObjectIds.Clear();
    }

    private void EnsureLayoutCaches(DocumentLayout layout)
    {
        if (ReferenceEquals(_cachedLayout, layout))
        {
            return;
        }

        _cachedLayout = layout;
        _verticalLineIndices = BuildVerticalLineIndices(layout);
        _tablesByPage = BuildTablesByPage(layout);
    }

    private static int[] BuildVerticalLineIndices(DocumentLayout layout)
    {
        if (layout.Lines.Count == 0)
        {
            return Array.Empty<int>();
        }

        var indices = new List<int>();
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            if (DocTextDirectionHelpers.IsVertical(layout.Lines[i].TextDirection))
            {
                indices.Add(i);
            }
        }

        return indices.Count == 0 ? Array.Empty<int>() : indices.ToArray();
    }

    private static int[][] BuildTablesByPage(DocumentLayout layout)
    {
        var pageCount = layout.Pages.Count;
        if (pageCount == 0 || layout.Tables.Count == 0)
        {
            return Array.Empty<int[]>();
        }

        var buckets = new List<int>[pageCount];
        for (var i = 0; i < pageCount; i++)
        {
            buckets[i] = new List<int>();
        }

        var pageFlow = layout.Settings.PageFlow;
        for (var i = 0; i < layout.Tables.Count; i++)
        {
            var bounds = layout.Tables[i].Bounds;
            var centerX = bounds.Left + bounds.Width * 0.5f;
            var centerY = bounds.Top + bounds.Height * 0.5f;
            var pageIndex = FindPageIndexForPoint(layout.Pages, centerX, centerY, pageFlow);
            if (pageIndex < 0 || pageIndex >= pageCount)
            {
                continue;
            }

            buckets[pageIndex].Add(i);
        }

        var result = new int[pageCount][];
        for (var i = 0; i < pageCount; i++)
        {
            var list = buckets[i];
            result[i] = list.Count == 0 ? Array.Empty<int>() : list.ToArray();
        }

        return result;
    }

    private static int FindPageIndexForPoint(IReadOnlyList<PageLayout> pages, float x, float y, PageFlowDirection flow)
    {
        if (pages.Count == 0)
        {
            return -1;
        }

        var axis = flow == PageFlowDirection.Horizontal ? x : y;
        var index = FindPageIndexByAxis(pages, axis, flow);
        if (index < 0 || index >= pages.Count)
        {
            return -1;
        }

        if (pages[index].Bounds.Contains(x, y))
        {
            return index;
        }

        if (index > 0 && pages[index - 1].Bounds.Contains(x, y))
        {
            return index - 1;
        }

        if (index + 1 < pages.Count && pages[index + 1].Bounds.Contains(x, y))
        {
            return index + 1;
        }

        return index;
    }

    private static int FindPageIndexByAxis(IReadOnlyList<PageLayout> pages, float value, PageFlowDirection flow)
    {
        var low = 0;
        var high = pages.Count - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var bounds = pages[mid].Bounds;
            var start = flow == PageFlowDirection.Horizontal ? bounds.Left : bounds.Top;
            var end = flow == PageFlowDirection.Horizontal ? bounds.Right : bounds.Bottom;
            if (value < start)
            {
                high = mid - 1;
            }
            else if (value >= end)
            {
                low = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        return Math.Clamp(low, 0, pages.Count - 1);
    }

    private bool TrySetCaretFromTable(float x, float y, SelectionUpdateMode mode)
    {
        var layout = _layoutService.Layout;
        if (layout.Tables.Count == 0)
        {
            return false;
        }

        EnsureLayoutCaches(layout);
        if (_tablesByPage.Length == 0)
        {
            return false;
        }

        var pageIndex = FindPageIndexForPoint(layout.Pages, x, y, layout.Settings.PageFlow);
        if (pageIndex < 0 || pageIndex >= _tablesByPage.Length)
        {
            return false;
        }

        var tableIndices = _tablesByPage[pageIndex];
        for (var tableIndex = 0; tableIndex < tableIndices.Length; tableIndex++)
        {
            var table = layout.Tables[tableIndices[tableIndex]];
            if (TrySetCaretFromTableLayout(table, x, y, mode))
            {
                return true;
            }
        }

        return false;
    }

    private bool TrySetCaretFromTableLayout(TableLayout table, float x, float y, SelectionUpdateMode mode)
    {
        if (!table.Bounds.Contains(x, y))
        {
            return false;
        }

        foreach (var cell in table.Cells)
        {
            if (!cell.Bounds.Contains(x, y))
            {
                continue;
            }

            if (cell.Tables.Count > 0)
            {
                foreach (var nested in cell.Tables)
                {
                    if (TrySetCaretFromTableLayout(nested, x, y, mode))
                    {
                        return true;
                    }
                }
            }

            var line = FindTableLineAtPoint(cell.Lines, x, y);
            if (line is null)
            {
                return false;
            }

            var offset = GetOffsetFromLine(line, x, y);
            SetCaret(new TextPosition(line.ParagraphIndex, offset), mode);
            return true;
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

    private void MoveCaret(TextPosition position, SelectionUpdateMode mode)
    {
        var previousRanges = CaptureSelectionRanges();
        var previousFloating = CaptureFloatingSelection();
        var clamped = ClampPosition(position);
        var add = mode.HasFlag(SelectionUpdateMode.Add);
        var extend = mode.HasFlag(SelectionUpdateMode.Extend);

        if (!extend)
        {
            _selectionAnchor = clamped;
        }

        if (!add && !extend)
        {
            _selectionRanges.Clear();
        }

        if (_selectedFloatingObjectIds.Count > 0)
        {
            _selectedFloatingObjectIds.Clear();
        }

        Caret = clamped;
        Selection = new TextRange(_selectionAnchor, clamped);

        if (add && !extend)
        {
            _selectionRanges.Insert(0, Selection);
        }
        else if (_selectionRanges.Count == 0)
        {
            _selectionRanges.Add(Selection);
        }
        else
        {
            _selectionRanges[0] = Selection;
        }

        UpdateTableSelections();

        var dirty = new HashSet<int>();
        AddDirtyPagesForRanges(dirty, previousRanges);
        AddDirtyPagesForRanges(dirty, _selectionRanges);
        AddDirtyPagesForFloatingSelection(dirty, previousFloating);
        AddDirtyPagesForFloatingSelection(dirty, _selectedFloatingObjectIds);
        RaiseSelectionChanged(dirty);
    }

    private static bool TryGetWordSpanAtOffset(ReadOnlySpan<char> text, int offset, out int start, out int length)
    {
        start = 0;
        length = 0;
        if (text.IsEmpty)
        {
            return false;
        }

        if (offset >= text.Length)
        {
            offset = text.Length - 1;
        }

        if (offset < 0)
        {
            offset = 0;
        }

        if (!IsWordChar(text[offset]))
        {
            if (offset > 0 && IsWordChar(text[offset - 1]))
            {
                offset--;
            }
            else
            {
                return false;
            }
        }

        var spanStart = offset;
        while (spanStart > 0 && IsWordChar(text[spanStart - 1]))
        {
            spanStart--;
        }

        var spanEnd = offset;
        while (spanEnd < text.Length && IsWordChar(text[spanEnd]))
        {
            spanEnd++;
        }

        if (spanEnd <= spanStart)
        {
            return false;
        }

        start = spanStart;
        length = spanEnd - spanStart;
        return true;
    }

    private static bool IsWordChar(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
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

    private int GetParagraphCount(DocumentLayout layout)
    {
        var count = layout.Paragraphs.Count;
        return count == 0 ? _document.ParagraphCount : count;
    }

    private ParagraphBlock GetParagraphAt(DocumentLayout layout, int paragraphIndex)
    {
        var paragraphs = layout.Paragraphs;
        if (paragraphIndex >= 0 && paragraphIndex < paragraphs.Count)
        {
            return paragraphs[paragraphIndex];
        }

        return _document.GetParagraph(paragraphIndex);
    }

    private TextPosition ClampPosition(TextPosition position)
    {
        var layout = _layoutService.Layout;
        var paragraphCount = layout.Paragraphs.Count;
        if (paragraphCount == 0)
        {
            if (_document.ParagraphCount == 0)
            {
                _document.Blocks.Add(new ParagraphBlock());
            }

            paragraphCount = _document.ParagraphCount;
        }

        var paragraphIndex = Math.Clamp(position.ParagraphIndex, 0, paragraphCount - 1);
        var paragraph = paragraphIndex < layout.Paragraphs.Count
            ? layout.Paragraphs[paragraphIndex]
            : _document.GetParagraph(paragraphIndex);
        var offset = Math.Clamp(position.Offset, 0, DocumentEditHelpers.GetParagraphLength(paragraph));
        return new TextPosition(paragraphIndex, offset);
    }

    private int FindLineIndexForCaret(out LayoutLine line)
    {
        var layout = _layoutService.Layout;
        var index = FindLineIndexForPosition(layout, Caret, out line);
        if (index < 0 || index >= layout.Lines.Count)
        {
            return index;
        }

        var lineEndOffset = line.StartOffset + line.Length;
        if (Caret.Offset == lineEndOffset && index + 1 < layout.Lines.Count)
        {
            var nextLine = layout.Lines[index + 1];
            if (nextLine.ParagraphIndex == line.ParagraphIndex && nextLine.StartOffset == lineEndOffset)
            {
                line = nextLine;
                return index + 1;
            }
        }

        return index;
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
        EnsureLayoutCaches(layout);
        var verticalLineIndices = _verticalLineIndices;
        for (var i = 0; i < verticalLineIndices.Length; i++)
        {
            var candidateIndex = verticalLineIndices[i];
            var candidate = layout.Lines[candidateIndex];

            if (IsPointWithinLine(candidate.X, candidate.Y, candidate.TextDirection, candidate.Width, candidate.LineHeight, x, y))
            {
                line = candidate;
                return candidateIndex;
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

    private List<CaretStop> BuildVisualCaretStops(LayoutLine line)
    {
        var segments = BuildVisualSegments(line.TextSpan, line.IsRtl, line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
        var stops = new List<CaretStop>();
        if (segments.Count == 0)
        {
            var lineStart = line.StartOffset;
            stops.Add(new CaretStop(lineStart, 0f));
            if (line.Length > 0)
            {
                stops.Add(new CaretStop(lineStart + line.Length, line.Width));
            }

            return stops;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            AppendSegmentCaretStops(line, segments[i], stops);
        }

        var lineStartOffset = line.StartOffset;
        var lineEndOffset = line.StartOffset + line.Length;
        if (stops.Count == 0 || stops[0].Offset != lineStartOffset)
        {
            stops.Insert(0, new CaretStop(lineStartOffset, 0f));
        }

        if (stops.Count == 0 || stops[^1].Offset != lineEndOffset)
        {
            var endX = segments[^1].X + segments[^1].Width;
            stops.Add(new CaretStop(lineEndOffset, endX));
        }

        return stops;
    }

    private void AppendSegmentCaretStops(LayoutLine line, VisualSegment segment, List<CaretStop> stops)
    {
        if (segment.Length <= 0)
        {
            return;
        }

        if (segment.IsText && segment.Run is not null)
        {
            AppendTextCaretStops(line, segment, stops);
            return;
        }

        AppendNonTextCaretStops(line, segment, stops);
    }

    private void AppendTextCaretStops(LayoutLine line, VisualSegment segment, List<CaretStop> stops)
    {
        var run = segment.Run!;
        var runStart = segment.RunStart;
        var runEnd = runStart + segment.Length;
        if (runEnd <= runStart)
        {
            return;
        }

        var metrics = GetRunMetrics(run.Text, run.Style, run.LetterSpacing);
        var clusterOffsets = metrics.ClusterOffsets;
        if (clusterOffsets.Length == 0)
        {
            AppendTextCaretStopsFallback(line, segment, runStart, runEnd, stops);
            return;
        }

        var startIndex = LowerBound(clusterOffsets, runStart);
        var endIndex = LowerBound(clusterOffsets, runEnd);
        if (segment.IsRtl)
        {
            AppendCaretStop(line, segment, runEnd, stops);
            for (var i = endIndex - 1; i >= startIndex; i--)
            {
                AppendCaretStop(line, segment, clusterOffsets[i], stops);
            }
        }
        else
        {
            for (var i = startIndex; i < endIndex; i++)
            {
                AppendCaretStop(line, segment, clusterOffsets[i], stops);
            }

            AppendCaretStop(line, segment, runEnd, stops);
        }
    }

    private void AppendTextCaretStopsFallback(
        LayoutLine line,
        VisualSegment segment,
        int runStart,
        int runEnd,
        List<CaretStop> stops)
    {
        var run = segment.Run!;
        var span = run.Text.AsSpan();
        var index = runStart;
        if (segment.IsRtl)
        {
            AppendCaretStop(line, segment, runEnd, stops);
            var offsets = new List<int>();
            while (index < runEnd)
            {
                offsets.Add(index);
                var length = TextCluster.GetNextClusterLength(span, index);
                index += Math.Max(1, length);
            }

            for (var i = offsets.Count - 1; i >= 0; i--)
            {
                AppendCaretStop(line, segment, offsets[i], stops);
            }
        }
        else
        {
            while (index < runEnd)
            {
                AppendCaretStop(line, segment, index, stops);
                var length = TextCluster.GetNextClusterLength(span, index);
                index += Math.Max(1, length);
            }

            AppendCaretStop(line, segment, runEnd, stops);
        }
    }

    private void AppendNonTextCaretStops(LayoutLine line, VisualSegment segment, List<CaretStop> stops)
    {
        var segmentStart = segment.RunStart;
        var segmentEnd = segmentStart + segment.Length;
        if (segment.IsRtl)
        {
            AppendCaretStop(line, segment, segmentEnd, stops);
            AppendCaretStop(line, segment, segmentStart, stops);
        }
        else
        {
            AppendCaretStop(line, segment, segmentStart, stops);
            AppendCaretStop(line, segment, segmentEnd, stops);
        }
    }

    private void AppendCaretStop(LayoutLine line, VisualSegment segment, int runOffset, List<CaretStop> stops)
    {
        var segmentOffset = runOffset - segment.RunStart;
        if (segmentOffset < 0 || segmentOffset > segment.Length)
        {
            return;
        }

        var lineOffset = line.StartOffset + segment.StartOffset + segmentOffset;
        var localX = segment.X + MeasureSegmentOffset(segment, segmentOffset);
        if (stops.Count > 0 && stops[^1].Offset == lineOffset)
        {
            return;
        }

        stops.Add(new CaretStop(lineOffset, localX));
    }

    private static int FindCaretStopIndex(IReadOnlyList<CaretStop> stops, int offset)
    {
        for (var i = 0; i < stops.Count; i++)
        {
            if (stops[i].Offset == offset)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindNearestCaretStopIndex(IReadOnlyList<CaretStop> stops, float localX)
    {
        if (stops.Count == 0)
        {
            return -1;
        }

        var bestIndex = 0;
        var bestDistance = MathF.Abs(stops[0].X - localX);
        for (var i = 1; i < stops.Count; i++)
        {
            var distance = MathF.Abs(stops[i].X - localX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static int LowerBound(ReadOnlySpan<int> values, int target)
    {
        var low = 0;
        var high = values.Length;
        while (low < high)
        {
            var mid = low + ((high - low) >> 1);
            if (values[mid] < target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
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

    private readonly record struct CaretStop(int Offset, float X);

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

        var offsetList = new List<int>(text.Length);
        var advanceList = new List<float>(text.Length);
        var span = text.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            offsetList.Add(index);
            var length = TextCluster.GetNextClusterLength(span, index);
            if (length <= 0)
            {
                length = 1;
            }

            var clusterText = text.Substring(index, length);
            advanceList.Add(_measurer.MeasureText(clusterText, style).Width);
            index += length;
        }

        return new TextShapeInfo(text.Length, offsetList.ToArray(), advanceList.ToArray());
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
        public int TextLength => _textLength;
        public ReadOnlySpan<int> ClusterOffsets => _clusterOffsets;

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

    private TextRange[] CaptureSelectionRanges()
    {
        if (_selectionRanges.Count == 0)
        {
            return Array.Empty<TextRange>();
        }

        var ranges = new TextRange[_selectionRanges.Count];
        _selectionRanges.CopyTo(ranges, 0);
        return ranges;
    }

    private Guid[] CaptureFloatingSelection()
    {
        if (_selectedFloatingObjectIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var ids = new Guid[_selectedFloatingObjectIds.Count];
        _selectedFloatingObjectIds.CopyTo(ids, 0);
        return ids;
    }

    private void UpdateTableSelections()
    {
        _tableSelections.Clear();
        if (_selectionRanges.Count == 0 || _document.ParagraphCount == 0)
        {
            return;
        }

        for (var i = 0; i < _selectionRanges.Count; i++)
        {
            var selection = _selectionRanges[i].Normalize();
            var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _document.ParagraphCount - 1);
            var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _document.ParagraphCount - 1);
            if (startIndex > endIndex)
            {
                (startIndex, endIndex) = (endIndex, startIndex);
            }

            var startLocation = _document.GetParagraphLocation(startIndex);
            var endLocation = _document.GetParagraphLocation(endIndex);
            if (!startLocation.IsInTable || !endLocation.IsInTable || startLocation.Table is null || endLocation.Table is null)
            {
                continue;
            }

            if (!ReferenceEquals(startLocation.Table, endLocation.Table))
            {
                continue;
            }

            var rowStart = Math.Min(startLocation.RowIndex, endLocation.RowIndex);
            var rowEnd = Math.Max(startLocation.RowIndex, endLocation.RowIndex);
            var columnStart = Math.Min(startLocation.ColumnIndex, endLocation.ColumnIndex);
            var columnEnd = Math.Max(startLocation.ColumnIndex, endLocation.ColumnIndex);
            var range = new TableSelectionRange(startLocation.Table, rowStart, rowEnd, columnStart, columnEnd).Normalize();

            if (!ContainsTableSelection(range))
            {
                _tableSelections.Add(range);
            }
        }
    }

    private bool ContainsTableSelection(TableSelectionRange range)
    {
        for (var i = 0; i < _tableSelections.Count; i++)
        {
            var existing = _tableSelections[i];
            if (ReferenceEquals(existing.Table, range.Table)
                && existing.RowStart == range.RowStart
                && existing.RowEnd == range.RowEnd
                && existing.ColumnStart == range.ColumnStart
                && existing.ColumnEnd == range.ColumnEnd)
            {
                return true;
            }
        }

        return false;
    }

    private void AddDirtyPagesForRanges(HashSet<int> dirty, IReadOnlyList<TextRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return;
        }

        for (var i = 0; i < ranges.Count; i++)
        {
            AddDirtyPagesForRange(dirty, ranges[i]);
        }
    }

    private void AddDirtyPagesForFloatingSelection(HashSet<int> dirty, IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var layout = _layoutService.Layout;
        if (layout.FloatingObjects.Count == 0)
        {
            return;
        }

        for (var i = 0; i < layout.FloatingObjects.Count; i++)
        {
            var floating = layout.FloatingObjects[i];
            if (!ContainsFloatingId(ids, floating.Object.Id))
            {
                continue;
            }

            if (floating.PageIndex >= 0)
            {
                dirty.Add(floating.PageIndex);
            }
        }
    }

    private static bool ContainsFloatingId(IReadOnlyList<Guid> ids, Guid id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return true;
            }
        }

        return false;
    }

    private void AddDirtyPagesForRange(HashSet<int> dirty, TextRange range)
    {
        var layout = _layoutService.Layout;
        if (layout.Pages.Count == 0)
        {
            return;
        }

        var normalized = range.Normalize();
        var startPage = GetPageForPosition(layout, normalized.Start);
        var endPage = GetPageForPosition(layout, normalized.End);
        if (startPage > endPage)
        {
            (startPage, endPage) = (endPage, startPage);
        }

        for (var pageIndex = startPage; pageIndex <= endPage; pageIndex++)
        {
            dirty.Add(pageIndex);
        }
    }

    private static int GetPageForPosition(DocumentLayout layout, TextPosition position)
    {
        if (layout.Lines.Count == 0)
        {
            return 0;
        }

        var lineIndex = FindLineIndexForPosition(layout, position, out _);
        var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
        return pageIndex < 0 ? 0 : pageIndex;
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

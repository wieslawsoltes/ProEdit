using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor;

public sealed class EditorController
{
    private readonly DocumentLayouter _layouter = new DocumentLayouter();
    private readonly ITextMeasurer _measurer;
    private TextPosition _selectionAnchor;
    private readonly Dictionary<RunMetricsKey, RunMetrics> _runMetricsCache = new();

    public Document Document { get; }
    public LayoutSettings LayoutSettings { get; } = new LayoutSettings();
    public DocumentLayout Layout { get; private set; }
    public TextPosition Caret { get; private set; }
    public TextRange Selection { get; private set; }
    public Guid? SelectedFloatingObjectId { get; private set; }
    public IReadOnlyList<int> DirtyPages { get; private set; } = Array.Empty<int>();
    public long DirtyVersion { get; private set; }

    public event EventHandler? Changed;

    public EditorController(ITextMeasurer measurer, Document? document = null)
    {
        _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
        Document = document ?? new Document();
        Caret = new TextPosition(0, 0);
        _selectionAnchor = Caret;
        Selection = new TextRange(Caret, Caret);
        Layout = _layouter.Layout(Document, LayoutSettings, _measurer);
        DirtyPages = Layout.Pages.Count == 0 ? Array.Empty<int>() : Enumerable.Range(0, Layout.Pages.Count).ToArray();
        DirtyVersion = 1;
    }

    public void UpdateLayout(float viewportWidth, float viewportHeight)
    {
        LayoutSettings.ViewportWidth = viewportWidth;
        LayoutSettings.ViewportHeight = viewportHeight;
        Reflow(null);
    }

    public void RefreshLayout()
    {
        Reflow(null);
    }

    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        DeleteSelectionIfAny();

        var paragraph = Document.GetParagraph(Caret.ParagraphIndex);
        InsertTextAtPosition(paragraph, Caret.Offset, text);
        MoveCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset + text.Length), false);
        Reflow(dirtyParagraphIndex);
    }

    public void InsertEquation(MathElement root, TextStyleProperties? style = null, string? styleId = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        DeleteSelectionIfAny();

        var paragraph = Document.GetParagraph(Caret.ParagraphIndex);
        if (style is null && styleId is null)
        {
            EnsureParagraphInlines(paragraph);
            if (paragraph.Inlines.Count > 0)
            {
                var position = FindInlinePosition(paragraph, Caret.Offset);
                if (paragraph.Inlines[position.Index] is RunInline run && run.Text.Length > 0)
                {
                    style = run.Style?.Clone();
                    styleId = run.StyleId;
                }
                else
                {
                    var insertIndex = position.OffsetInInline <= 0 ? position.Index : position.Index + 1;
                    var (adjacentStyle, adjacentStyleId) = GetAdjacentRunStyle(paragraph, insertIndex);
                    style = adjacentStyle;
                    styleId = adjacentStyleId;
                }
            }
        }

        var equation = new EquationInline(root)
        {
            Style = style,
            StyleId = styleId
        };

        InsertInlineAtPosition(paragraph, Caret.Offset, equation);
        MoveCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset + 1), false);
        Reflow(dirtyParagraphIndex);
    }

    public void InsertParagraphBreak()
    {
        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        DeleteSelectionIfAny();

        var location = Document.GetParagraphLocation(Caret.ParagraphIndex);
        var paragraph = location.Paragraph;
        var offset = Caret.Offset;
        var newParagraph = new ParagraphBlock(string.Empty, paragraph.ListInfo?.Clone())
        {
            StyleId = paragraph.StyleId
        };
        CopyParagraphProperties(paragraph.Properties, newParagraph.Properties);

        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            var before = text.Substring(0, offset);
            var after = text.Substring(offset);
            paragraph.Text = before;
            newParagraph.Text = after;
        }
        else
        {
            SplitInlinesAtOffset(paragraph, offset, out var before, out var after);
            paragraph.Inlines.Clear();
            paragraph.Inlines.AddRange(before);
            NormalizeInlines(paragraph);

            newParagraph.Inlines.AddRange(after);
            NormalizeInlines(newParagraph);
        }

        SplitFloatingAnchors(paragraph, newParagraph, offset);
        Document.InsertParagraphAfter(location, newParagraph);

        MoveCaret(new TextPosition(Caret.ParagraphIndex + 1, 0), false);
        Reflow(dirtyParagraphIndex);
    }

    public void Backspace()
    {
        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        if (DeleteSelectionIfAny())
        {
            Reflow(dirtyParagraphIndex);
            return;
        }

        if (Caret.Offset > 0)
        {
            var paragraph = Document.GetParagraph(Caret.ParagraphIndex);
            DeleteRangeInParagraph(paragraph, Caret.Offset - 1, Caret.Offset);
            MoveCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset - 1), false);
            Reflow(dirtyParagraphIndex);
            return;
        }

        if (Caret.ParagraphIndex > 0)
        {
            var currentLocation = Document.GetParagraphLocation(Caret.ParagraphIndex);
            var previousLocation = Document.GetParagraphLocation(Caret.ParagraphIndex - 1);
            var previous = previousLocation.Paragraph;
            if (!currentLocation.IsSameContainer(previousLocation))
            {
                MoveCaret(new TextPosition(Caret.ParagraphIndex - 1, GetParagraphLength(previous)), false);
                return;
            }

            var current = currentLocation.Paragraph;
            var newOffset = GetParagraphLength(previous);
            AppendParagraphContent(previous, current);
            Document.RemoveParagraphAt(currentLocation);
            MoveCaret(new TextPosition(Caret.ParagraphIndex - 1, newOffset), false);
            Reflow(Caret.ParagraphIndex - 1);
        }
    }

    public void DeleteForward()
    {
        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        if (DeleteSelectionIfAny())
        {
            Reflow(dirtyParagraphIndex);
            return;
        }

        var currentLocation = Document.GetParagraphLocation(Caret.ParagraphIndex);
        var paragraph = currentLocation.Paragraph;
        if (Caret.Offset < GetParagraphLength(paragraph))
        {
            DeleteRangeInParagraph(paragraph, Caret.Offset, Caret.Offset + 1);
            Reflow(dirtyParagraphIndex);
            return;
        }

        if (Caret.ParagraphIndex < Document.ParagraphCount - 1)
        {
            var nextLocation = Document.GetParagraphLocation(Caret.ParagraphIndex + 1);
            if (!currentLocation.IsSameContainer(nextLocation))
            {
                MoveCaret(new TextPosition(Caret.ParagraphIndex + 1, 0), false);
                return;
            }

            var next = nextLocation.Paragraph;
            AppendParagraphContent(paragraph, next);
            Document.RemoveParagraphAt(nextLocation);
            Reflow(dirtyParagraphIndex);
        }
    }

    public void MoveLeft(bool extendSelection)
    {
        if (!extendSelection && !Selection.IsEmpty)
        {
            MoveCaret(Selection.Normalize().Start, false);
            return;
        }

        if (Caret.Offset > 0)
        {
            MoveCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset - 1), extendSelection);
            return;
        }

        if (Caret.ParagraphIndex > 0)
        {
            var previous = Document.GetParagraph(Caret.ParagraphIndex - 1);
            MoveCaret(new TextPosition(Caret.ParagraphIndex - 1, GetParagraphLength(previous)), extendSelection);
        }
    }

    public void MoveRight(bool extendSelection)
    {
        if (!extendSelection && !Selection.IsEmpty)
        {
            MoveCaret(Selection.Normalize().End, false);
            return;
        }

        var paragraph = Document.GetParagraph(Caret.ParagraphIndex);
        if (Caret.Offset < GetParagraphLength(paragraph))
        {
            MoveCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset + 1), extendSelection);
            return;
        }

        if (Caret.ParagraphIndex < Document.ParagraphCount - 1)
        {
            MoveCaret(new TextPosition(Caret.ParagraphIndex + 1, 0), extendSelection);
        }
    }

    public void MoveUp(bool extendSelection)
    {
        if (Layout.Lines.Count == 0)
        {
            return;
        }

        var currentIndex = FindLineIndexForCaret(out var currentLine);
        if (currentIndex <= 0)
        {
            MoveCaret(new TextPosition(0, 0), extendSelection);
            return;
        }

        var targetLine = Layout.Lines[currentIndex - 1];
        var caretX = GetCaretX(currentLine);
        var offset = GetOffsetFromLine(targetLine, caretX);
        MoveCaret(new TextPosition(targetLine.ParagraphIndex, offset), extendSelection);
    }

    public void MoveDown(bool extendSelection)
    {
        if (Layout.Lines.Count == 0)
        {
            return;
        }

        var currentIndex = FindLineIndexForCaret(out var currentLine);
        if (currentIndex >= Layout.Lines.Count - 1)
        {
            var lastParagraphIndex = Document.ParagraphCount - 1;
            var lastParagraph = Document.GetParagraph(lastParagraphIndex);
            MoveCaret(new TextPosition(lastParagraphIndex, GetParagraphLength(lastParagraph)), extendSelection);
            return;
        }

        var targetLine = Layout.Lines[currentIndex + 1];
        var caretX = GetCaretX(currentLine);
        var offset = GetOffsetFromLine(targetLine, caretX);
        MoveCaret(new TextPosition(targetLine.ParagraphIndex, offset), extendSelection);
    }

    public void SetCaretFromPoint(float x, float y, bool extendSelection)
    {
        if (Layout.Lines.Count == 0)
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

        var lineIndex = Layout.LineIndex.FindLineAtY(y);
        if (lineIndex < 0 || lineIndex >= Layout.Lines.Count)
        {
            return;
        }

        var line = Layout.Lines[lineIndex];

        var offset = GetOffsetFromLine(line, x);
        MoveCaret(new TextPosition(line.ParagraphIndex, offset), extendSelection);
    }

    public EquationInline? GetEquationAtCaret()
    {
        return GetEquationAtPosition(Caret);
    }

    public EquationInline? GetEquationAtPosition(TextPosition position)
    {
        if (position.ParagraphIndex < 0 || position.ParagraphIndex >= Document.ParagraphCount)
        {
            return null;
        }

        var paragraph = Document.GetParagraph(position.ParagraphIndex);
        return FindEquationInline(paragraph, position.Offset);
    }

    private bool TrySelectFloatingObject(float x, float y)
    {
        if (Layout.FloatingObjects.Count == 0)
        {
            return false;
        }

        for (var i = Layout.FloatingObjects.Count - 1; i >= 0; i--)
        {
            var floating = Layout.FloatingObjects[i];
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

        var dirty = new HashSet<int>(ComputeDirtyPagesForSelection(previousSelection, Selection, Layout));
        if (pageIndex >= 0)
        {
            dirty.Add(pageIndex);
        }

        DirtyPages = dirty.Count == 0 ? Array.Empty<int>() : dirty.ToArray();
        DirtyVersion++;
        OnChanged();
    }

    private void ClearFloatingSelection()
    {
        SelectedFloatingObjectId = null;
    }

    private bool TrySetCaretFromTable(float x, float y, bool extendSelection)
    {
        foreach (var table in Layout.Tables)
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

                var line = FindTableLineAtPoint(cell.Lines, y);
                if (line is null)
                {
                    return false;
                }

                var offset = GetOffsetFromLine(line, x);
                MoveCaret(new TextPosition(line.ParagraphIndex, offset), extendSelection);
                return true;
            }
        }

        return false;
    }

    private static TableCellLine? FindTableLineAtPoint(IReadOnlyList<TableCellLine> lines, float y)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        foreach (var line in lines)
        {
            if (y >= line.Y && y <= line.Y + line.LineHeight)
            {
                return line;
            }
        }

        return y < lines[0].Y ? lines[0] : lines[^1];
    }

    private int GetOffsetFromLine(TableCellLine line, float x)
    {
        var relativeX = x - line.X;
        if (relativeX <= 0)
        {
            return line.StartOffset;
        }

        if (line.Text.Length == 0)
        {
            return line.StartOffset;
        }

        var offsetInLine = 0;
        var remainingX = relativeX;
        foreach (var segment in EnumerateSegments(line.Runs, line.Images, line.Shapes, line.Charts, line.Equations))
        {
            var segmentWidth = GetSegmentWidth(segment);
            if (remainingX <= segmentWidth)
            {
                if (segment.IsTab || segment.IsImage || segment.IsShape || segment.IsChart || segment.IsEquation)
                {
                    var advance = remainingX >= segmentWidth / 2f ? 1 : 0;
                    return line.StartOffset + offsetInLine + advance;
                }

                var metrics = GetRunMetrics(segment.Text, segment.Style, segment.LetterSpacing);
                var localOffset = metrics.GetOffsetForX(remainingX);
                return line.StartOffset + offsetInLine + localOffset;
            }

            remainingX -= segmentWidth;
            offsetInLine += segment.Length;
        }

        return line.StartOffset + offsetInLine;
    }

    private void MoveCaret(TextPosition position, bool extendSelection)
    {
        var previousSelection = Selection;
        var previousFloating = SelectedFloatingObjectId;
        var clamped = ClampPosition(position);
        Caret = clamped;

        if (!extendSelection)
        {
            _selectionAnchor = clamped;
        }

        if (previousFloating.HasValue)
        {
            SelectedFloatingObjectId = null;
        }

        Selection = new TextRange(_selectionAnchor, clamped);
        var dirty = new HashSet<int>(ComputeDirtyPagesForSelection(previousSelection, Selection, Layout));
        if (previousFloating.HasValue)
        {
            var pageIndex = ResolveFloatingPageIndex(previousFloating.Value);
            if (pageIndex.HasValue)
            {
                dirty.Add(pageIndex.Value);
            }
        }

        DirtyPages = dirty.Count == 0 ? Array.Empty<int>() : dirty.ToArray();
        DirtyVersion++;
        OnChanged();
    }

    private int? ResolveFloatingPageIndex(Guid id)
    {
        foreach (var floating in Layout.FloatingObjects)
        {
            if (floating.Object.Id == id)
            {
                return floating.PageIndex;
            }
        }

        return null;
    }

    private void Reflow(int? dirtyParagraphIndex)
    {
        var previousLayout = Layout;
        Layout = _layouter.Layout(Document, LayoutSettings, _measurer, previousLayout, dirtyParagraphIndex);
        DirtyPages = ComputeDirtyPages(previousLayout, Layout, dirtyParagraphIndex);
        DirtyVersion++;
        OnChanged();
    }

    private bool DeleteSelectionIfAny()
    {
        if (Selection.IsEmpty)
        {
            return false;
        }

        var range = Selection.Normalize();
        if (range.Start.ParagraphIndex == range.End.ParagraphIndex)
        {
            var paragraph = Document.GetParagraph(range.Start.ParagraphIndex);
            DeleteRangeInParagraph(paragraph, range.Start.Offset, range.End.Offset);
        }
        else
        {
            var startLocation = Document.GetParagraphLocation(range.Start.ParagraphIndex);
            var endLocation = Document.GetParagraphLocation(range.End.ParagraphIndex);
            var startParagraph = startLocation.Paragraph;
            var endParagraph = endLocation.Paragraph;
            var startLength = GetParagraphLength(startParagraph);
            DeleteRangeInParagraph(startParagraph, range.Start.Offset, startLength);
            DeleteRangeInParagraph(endParagraph, 0, range.End.Offset);
            if (startLocation.IsSameContainer(endLocation))
            {
                AppendParagraphContent(startParagraph, endParagraph);

                for (var i = range.End.ParagraphIndex; i > range.Start.ParagraphIndex; i--)
                {
                    Document.RemoveParagraphAt(i);
                }
            }
            else
            {
                for (var i = range.End.ParagraphIndex - 1; i > range.Start.ParagraphIndex; i--)
                {
                    Document.RemoveParagraphAt(i);
                }
            }
        }

        MoveCaret(new TextPosition(range.Start.ParagraphIndex, range.Start.Offset), false);
        return true;
    }

    private int GetDirtyParagraphIndex()
    {
        if (Selection.IsEmpty)
        {
            return Caret.ParagraphIndex;
        }

        return Selection.Normalize().Start.ParagraphIndex;
    }

    private IReadOnlyList<int> ComputeDirtyPages(DocumentLayout? previousLayout, DocumentLayout currentLayout, int? dirtyParagraphIndex)
    {
        if (currentLayout.Pages.Count == 0)
        {
            return Array.Empty<int>();
        }

        if (previousLayout is null || !dirtyParagraphIndex.HasValue || dirtyParagraphIndex.Value < 0)
        {
            return Enumerable.Range(0, currentLayout.Pages.Count).ToArray();
        }

        if (!previousLayout.ParagraphLineRanges.TryGetValue(dirtyParagraphIndex.Value, out var range) || range.Count == 0)
        {
            return Enumerable.Range(0, currentLayout.Pages.Count).ToArray();
        }

        var startPage = previousLayout.LineIndex.GetPageForLine(range.Start);
        if (startPage < 0 || startPage >= currentLayout.Pages.Count)
        {
            return Enumerable.Range(0, currentLayout.Pages.Count).ToArray();
        }

        return Enumerable.Range(startPage, currentLayout.Pages.Count - startPage).ToArray();
    }

    private IReadOnlyList<int> ComputeDirtyPagesForSelection(TextRange previousSelection, TextRange currentSelection, DocumentLayout layout)
    {
        if (layout.Pages.Count == 0)
        {
            return Array.Empty<int>();
        }

        var previousRange = GetPageRangeForSelection(previousSelection, layout);
        var currentRange = GetPageRangeForSelection(currentSelection, layout);
        var start = Math.Min(previousRange.Start, currentRange.Start);
        var end = Math.Max(previousRange.End, currentRange.End);
        var count = Math.Max(0, end - start + 1);
        return count == 0 ? Array.Empty<int>() : Enumerable.Range(start, count).ToArray();
    }

    private static PageRange GetPageRangeForSelection(TextRange selection, DocumentLayout layout)
    {
        var startPage = GetPageForPosition(selection.Start, layout);
        var endPage = GetPageForPosition(selection.End, layout);
        if (startPage > endPage)
        {
            (startPage, endPage) = (endPage, startPage);
        }

        return new PageRange(startPage, endPage);
    }

    private static int GetPageForPosition(TextPosition position, DocumentLayout layout)
    {
        if (layout.Lines.Count == 0)
        {
            return 0;
        }

        var lineIndex = FindLineIndexForPosition(layout, position, out _);
        var pageIndex = layout.LineIndex.GetPageForLine(lineIndex);
        return pageIndex < 0 ? 0 : pageIndex;
    }

    private readonly struct PageRange
    {
        public int Start { get; }
        public int End { get; }

        public PageRange(int start, int end)
        {
            Start = start;
            End = end;
        }
    }

    private void InsertTextAtPosition(ParagraphBlock paragraph, int offset, string text)
    {
        EnsureParagraphInlines(paragraph);
        ShiftFloatingAnchorsOnInsert(paragraph, offset, text.Length);
        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Inlines.Add(new RunInline(text));
            UpdateParagraphText(paragraph);
            return;
        }

        var position = FindInlinePosition(paragraph, offset);
        var inline = paragraph.Inlines[position.Index];
        if (inline is RunInline run)
        {
            var insertAt = Math.Clamp(position.OffsetInInline, 0, run.Text.Length);
            run.Text.Insert(insertAt, text);
        }
        else
        {
            var insertIndex = position.OffsetInInline <= 0 ? position.Index : position.Index + 1;
            var (style, styleId) = GetAdjacentRunStyle(paragraph, insertIndex);
            paragraph.Inlines.Insert(insertIndex, new RunInline(text, style) { StyleId = styleId });
        }

        NormalizeInlines(paragraph);
    }

    private void InsertInlineAtPosition(ParagraphBlock paragraph, int offset, Inline inline)
    {
        EnsureParagraphInlines(paragraph);
        var inlineLength = GetInlineLength(inline);
        ShiftFloatingAnchorsOnInsert(paragraph, offset, inlineLength);
        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Inlines.Add(inline);
            UpdateParagraphText(paragraph);
            return;
        }

        var position = FindInlinePosition(paragraph, offset);
        var current = paragraph.Inlines[position.Index];
        if (current is RunInline run)
        {
            var insertAt = Math.Clamp(position.OffsetInInline, 0, run.Text.Length);
            if (insertAt <= 0)
            {
                paragraph.Inlines.Insert(position.Index, inline);
            }
            else if (insertAt >= run.Text.Length)
            {
                paragraph.Inlines.Insert(position.Index + 1, inline);
            }
            else
            {
                var beforeText = run.Text.SliceBuffer(0, insertAt);
                var afterText = run.Text.SliceBuffer(insertAt, run.Text.Length - insertAt);
                var beforeRun = new RunInline(beforeText, run.Style) { StyleId = run.StyleId };
                var afterRun = new RunInline(afterText, run.Style) { StyleId = run.StyleId };
                paragraph.Inlines.RemoveAt(position.Index);
                paragraph.Inlines.Insert(position.Index, beforeRun);
                paragraph.Inlines.Insert(position.Index + 1, inline);
                paragraph.Inlines.Insert(position.Index + 2, afterRun);
            }
        }
        else
        {
            var insertIndex = position.OffsetInInline <= 0 ? position.Index : position.Index + 1;
            paragraph.Inlines.Insert(insertIndex, inline);
        }

        NormalizeInlines(paragraph);
    }

    private static void ShiftFloatingAnchorsOnInsert(ParagraphBlock paragraph, int offset, int length)
    {
        if (length <= 0 || paragraph.FloatingObjects.Count == 0)
        {
            return;
        }

        foreach (var floating in paragraph.FloatingObjects)
        {
            if (floating.Anchor.AnchorOffset is not { } anchorOffset)
            {
                continue;
            }

            if (anchorOffset >= offset)
            {
                floating.Anchor.AnchorOffset = anchorOffset + length;
            }
        }
    }

    private static int GetParagraphLength(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return (paragraph.Text ?? string.Empty).Length;
        }

        var length = 0;
        foreach (var inline in paragraph.Inlines)
        {
            length += GetInlineLength(inline);
        }

        return length;
    }

    private static EquationInline? FindEquationInline(ParagraphBlock paragraph, int offset)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return null;
        }

        var position = 0;
        foreach (var inline in paragraph.Inlines)
        {
            var inlineLength = GetInlineLength(inline);
            if (offset >= position && offset < position + inlineLength)
            {
                return inline as EquationInline;
            }

            position += inlineLength;
        }

        return null;
    }

    private static int GetInlineLength(Inline inline)
    {
        return inline switch
        {
            RunInline run => run.Text.Length,
            ImageInline => 1,
            ShapeInline => 1,
            ChartInline => 1,
            EquationInline => 1,
            PageNumberInline => 1,
            FootnoteReferenceInline footnote => footnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length,
            EndnoteReferenceInline endnote => endnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length,
            CommentReferenceInline comment => comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length,
            FieldStartInline => 0,
            FieldSeparatorInline => 0,
            FieldEndInline => 0,
            BookmarkStartInline => 0,
            BookmarkEndInline => 0,
            CommentRangeStartInline => 0,
            CommentRangeEndInline => 0,
            ContentControlStartInline => 0,
            ContentControlEndInline => 0,
            _ => 1
        };
    }

    private static void EnsureParagraphInlines(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count > 0)
        {
            return;
        }

        var text = paragraph.Text ?? string.Empty;
        if (text.Length > 0)
        {
            paragraph.Inlines.Add(new RunInline(text));
        }
    }

    private static void UpdateParagraphText(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return;
        }

        paragraph.Text = BuildInlineText(paragraph.Inlines);
    }

    private static string BuildInlineText(IEnumerable<Inline> inlines)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case RunInline run:
                    builder.Append(run.Text.GetText());
                    break;
                case ImageInline:
                case ShapeInline:
                case ChartInline:
                case EquationInline:
                case PageNumberInline:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
                case FootnoteReferenceInline footnote:
                    builder.Append(footnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case EndnoteReferenceInline endnote:
                    builder.Append(endnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case CommentReferenceInline comment:
                    builder.Append(comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case FieldStartInline:
                case FieldSeparatorInline:
                case FieldEndInline:
                case BookmarkStartInline:
                case BookmarkEndInline:
                case CommentRangeStartInline:
                case CommentRangeEndInline:
                case ContentControlStartInline:
                case ContentControlEndInline:
                    break;
                default:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
            }
        }

        return builder.ToString();
    }

    private static InlinePosition FindInlinePosition(ParagraphBlock paragraph, int offset)
    {
        var inlines = paragraph.Inlines;
        if (inlines.Count == 0)
        {
            return new InlinePosition(0, 0, 0);
        }

        var position = 0;
        for (var i = 0; i < inlines.Count; i++)
        {
            var length = GetInlineLength(inlines[i]);
            var end = position + length;
            if (offset <= end)
            {
                return new InlinePosition(i, Math.Max(0, offset - position), length);
            }

            position = end;
        }

        var lastIndex = inlines.Count - 1;
        var lastLength = GetInlineLength(inlines[lastIndex]);
        return new InlinePosition(lastIndex, lastLength, lastLength);
    }

    private static void SplitInlinesAtOffset(ParagraphBlock paragraph, int offset, out List<Inline> before, out List<Inline> after)
    {
        before = new List<Inline>();
        after = new List<Inline>();

        var length = GetParagraphLength(paragraph);
        var splitOffset = Math.Clamp(offset, 0, length);
        var position = 0;

        foreach (var inline in paragraph.Inlines)
        {
            var inlineLength = GetInlineLength(inline);
            var end = position + inlineLength;
            if (splitOffset <= position)
            {
                after.Add(inline);
            }
            else if (splitOffset >= end)
            {
                before.Add(inline);
            }
            else if (inline is RunInline run)
            {
                var runLength = run.Text.Length;
                var splitIndex = Math.Clamp(splitOffset - position, 0, runLength);
                if (splitIndex > 0)
                {
                    before.Add(new RunInline(run.Text.SliceBuffer(0, splitIndex), run.Style) { StyleId = run.StyleId });
                }

                var afterLength = runLength - splitIndex;
                if (afterLength > 0)
                {
                    after.Add(new RunInline(run.Text.SliceBuffer(splitIndex, afterLength), run.Style) { StyleId = run.StyleId });
                }
            }
            else
            {
                before.Add(inline);
            }

            position = end;
        }
    }

    private static void SplitFloatingAnchors(ParagraphBlock source, ParagraphBlock target, int splitOffset)
    {
        if (source.FloatingObjects.Count == 0)
        {
            return;
        }

        for (var i = source.FloatingObjects.Count - 1; i >= 0; i--)
        {
            var floating = source.FloatingObjects[i];
            if (floating.Anchor.AnchorOffset is not { } anchorOffset)
            {
                continue;
            }

            if (anchorOffset >= splitOffset)
            {
                floating.Anchor.AnchorOffset = Math.Max(0, anchorOffset - splitOffset);
                source.FloatingObjects.RemoveAt(i);
                target.FloatingObjects.Add(floating);
            }
        }
    }

    private void DeleteRangeInParagraph(ParagraphBlock paragraph, int startOffset, int endOffset)
    {
        var length = GetParagraphLength(paragraph);
        var start = Math.Clamp(startOffset, 0, length);
        var end = Math.Clamp(endOffset, 0, length);
        if (end <= start)
        {
            return;
        }

        ShiftFloatingAnchorsOnDelete(paragraph, start, end);
        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            paragraph.Text = text.Remove(start, end - start);
            return;
        }

        var newInlines = new List<Inline>(paragraph.Inlines.Count);
        var position = 0;
        foreach (var inline in paragraph.Inlines)
        {
            var inlineLength = GetInlineLength(inline);
            var inlineEnd = position + inlineLength;
            if (inlineEnd <= start || position >= end)
            {
                newInlines.Add(inline);
            }
            else if (inline is RunInline run)
            {
                var runLength = run.Text.Length;
                var deleteStart = Math.Clamp(start - position, 0, runLength);
                var deleteEnd = Math.Clamp(end - position, 0, runLength);
                if (deleteStart > 0)
                {
                    newInlines.Add(new RunInline(run.Text.SliceBuffer(0, deleteStart), run.Style) { StyleId = run.StyleId });
                }

                var afterLength = runLength - deleteEnd;
                if (afterLength > 0)
                {
                    newInlines.Add(new RunInline(run.Text.SliceBuffer(deleteEnd, afterLength), run.Style) { StyleId = run.StyleId });
                }
            }

            position = inlineEnd;
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(newInlines);
        NormalizeInlines(paragraph);
    }

    private static void ShiftFloatingAnchorsOnDelete(ParagraphBlock paragraph, int start, int end)
    {
        if (paragraph.FloatingObjects.Count == 0 || end <= start)
        {
            return;
        }

        var delta = end - start;
        foreach (var floating in paragraph.FloatingObjects)
        {
            if (floating.Anchor.AnchorOffset is not { } anchorOffset)
            {
                continue;
            }

            if (anchorOffset >= end)
            {
                floating.Anchor.AnchorOffset = anchorOffset - delta;
            }
            else if (anchorOffset >= start)
            {
                floating.Anchor.AnchorOffset = start;
            }
        }
    }

    private void AppendParagraphContent(ParagraphBlock target, ParagraphBlock source)
    {
        var targetLength = GetParagraphLength(target);
        if (source.Inlines.Count == 0)
        {
            var sourceText = source.Text ?? string.Empty;
            if (sourceText.Length == 0)
            {
                AppendFloatingAnchors(target, source, targetLength);
                return;
            }

            if (target.Inlines.Count == 0)
            {
                target.Text = (target.Text ?? string.Empty) + sourceText;
                AppendFloatingAnchors(target, source, targetLength);
                return;
            }

            target.Inlines.Add(new RunInline(sourceText));
            NormalizeInlines(target);
            AppendFloatingAnchors(target, source, targetLength);
            return;
        }

        EnsureParagraphInlines(target);
        foreach (var inline in source.Inlines)
        {
            target.Inlines.Add(inline);
        }

        NormalizeInlines(target);
        AppendFloatingAnchors(target, source, targetLength);
    }

    private static void AppendFloatingAnchors(ParagraphBlock target, ParagraphBlock source, int targetLength)
    {
        if (source.FloatingObjects.Count == 0)
        {
            return;
        }

        foreach (var floating in source.FloatingObjects)
        {
            if (floating.Anchor.AnchorOffset is { } anchorOffset)
            {
                floating.Anchor.AnchorOffset = anchorOffset + targetLength;
            }

            target.FloatingObjects.Add(floating);
        }

        source.FloatingObjects.Clear();
    }

    private static void NormalizeInlines(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Text = string.Empty;
            return;
        }

        var normalized = new List<Inline>(paragraph.Inlines.Count);
        RunInline? lastRun = null;

        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run)
            {
                if (run.Text.Length == 0)
                {
                    continue;
                }

                if (lastRun is not null && AreRunsMergeable(lastRun, run))
                {
                    lastRun.Text.Append(run.Text);
                }
                else
                {
                    normalized.Add(run);
                    lastRun = run;
                }
            }
            else
            {
                normalized.Add(inline);
                lastRun = null;
            }
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(normalized);
        UpdateParagraphText(paragraph);
    }

    private static (TextStyleProperties? Style, string? StyleId) GetAdjacentRunStyle(ParagraphBlock paragraph, int insertIndex)
    {
        var inlines = paragraph.Inlines;
        for (var i = insertIndex - 1; i >= 0; i--)
        {
            if (inlines[i] is RunInline run && run.Text.Length > 0)
            {
                return (run.Style?.Clone(), run.StyleId);
            }
        }

        for (var i = insertIndex; i < inlines.Count; i++)
        {
            if (inlines[i] is RunInline run && run.Text.Length > 0)
            {
                return (run.Style?.Clone(), run.StyleId);
            }
        }

        return (null, null);
    }

    private static bool AreRunsMergeable(RunInline left, RunInline right)
    {
        if (!string.Equals(left.StyleId, right.StyleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (left.Style is null && right.Style is null)
        {
            return true;
        }

        if (left.Style is null || right.Style is null)
        {
            return false;
        }

        return AreTextStylesEquivalent(left.Style, right.Style);
    }

    private static bool AreTextStylesEquivalent(TextStyleProperties left, TextStyleProperties right)
    {
        return left.IsEquivalentTo(right);
    }

    private static void CopyParagraphProperties(ParagraphProperties source, ParagraphProperties target)
    {
        target.Alignment = source.Alignment;
        target.SpacingBefore = source.SpacingBefore;
        target.SpacingAfter = source.SpacingAfter;
        target.LineSpacing = source.LineSpacing;
        target.LineSpacingRule = source.LineSpacingRule;
        target.IndentLeft = source.IndentLeft;
        target.IndentRight = source.IndentRight;
        target.FirstLineIndent = source.FirstLineIndent;
        target.KeepWithNext = source.KeepWithNext;
        target.KeepLinesTogether = source.KeepLinesTogether;
        target.WidowControl = source.WidowControl;
        target.PageBreakBefore = source.PageBreakBefore;
        target.ContextualSpacing = source.ContextualSpacing;
        target.Bidi = source.Bidi;
        target.ShadingColor = source.ShadingColor;
        if (source.Borders.HasAny)
        {
            target.Borders.Top = source.Borders.Top?.Clone();
            target.Borders.Bottom = source.Borders.Bottom?.Clone();
            target.Borders.Left = source.Borders.Left?.Clone();
            target.Borders.Right = source.Borders.Right?.Clone();
        }
        target.TabStops.Clear();
        foreach (var tabStop in source.TabStops)
        {
            target.TabStops.Add(tabStop.Clone());
        }
    }

    private readonly struct InlinePosition
    {
        public int Index { get; }
        public int OffsetInInline { get; }
        public int Length { get; }

        public InlinePosition(int index, int offsetInInline, int length)
        {
            Index = index;
            OffsetInInline = offsetInInline;
            Length = length;
        }
    }

    private TextPosition ClampPosition(TextPosition position)
    {
        if (Document.ParagraphCount == 0)
        {
            Document.Blocks.Add(new ParagraphBlock());
        }

        var paragraphIndex = Math.Clamp(position.ParagraphIndex, 0, Document.ParagraphCount - 1);
        var paragraph = Document.GetParagraph(paragraphIndex);
        var offset = Math.Clamp(position.Offset, 0, GetParagraphLength(paragraph));
        return new TextPosition(paragraphIndex, offset);
    }

    private int FindLineIndexForCaret(out LayoutLine line)
    {
        return FindLineIndexForPosition(Layout, Caret, out line);
    }

    private float GetCaretX(LayoutLine line)
    {
        var offsetInLine = Math.Clamp(Caret.Offset - line.StartOffset, 0, line.Length);
        var width = MeasureLineOffset(line, offsetInLine);
        return line.X + width;
    }

    private static int FindLineIndexForPosition(DocumentLayout layout, TextPosition position, out LayoutLine line)
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

    private int GetOffsetFromLine(LayoutLine line, float x)
    {
        var relativeX = x - line.X;
        if (relativeX <= 0)
        {
            return line.StartOffset;
        }

        var text = line.Text;
        if (text.Length == 0)
        {
            return line.StartOffset;
        }

        var offsetInLine = 0;
        foreach (var segment in EnumerateSegments(line))
        {
            var segmentWidth = GetSegmentWidth(segment);
            if (relativeX <= segmentWidth)
            {
                if (segment.IsTab || segment.IsImage || segment.IsShape || segment.IsChart || segment.IsEquation)
                {
                    var advance = relativeX >= segmentWidth / 2f ? 1 : 0;
                    return line.StartOffset + offsetInLine + advance;
                }

                var metrics = GetRunMetrics(segment.Text, segment.Style, segment.LetterSpacing);
                if (segment.Width > 0f && segment.Length == 1 && MathF.Abs(segment.Width - metrics.Width) > 0.01f)
                {
                    var advance = relativeX >= segment.Width / 2f ? 1 : 0;
                    return line.StartOffset + offsetInLine + advance;
                }

                var localOffset = metrics.GetOffsetForX(relativeX);
                return line.StartOffset + offsetInLine + localOffset;
            }

            relativeX -= segmentWidth;
            offsetInLine += segment.Length;
        }

        return line.StartOffset + offsetInLine;
    }

    private float MeasureLineOffset(LayoutLine line, int length)
    {
        if (length <= 0)
        {
            return 0f;
        }

        var remaining = length;
        var width = 0f;

        foreach (var segment in EnumerateSegments(line))
        {
            if (remaining <= 0)
            {
                break;
            }

            if (segment.IsImage || segment.IsShape || segment.IsChart || segment.IsEquation)
            {
                width += segment.Width;
                remaining -= segment.Length;
                continue;
            }

            if (segment.IsTab)
            {
                width += segment.Width;
                remaining -= segment.Length;
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

                var metrics = GetRunMetrics(segment.Text, segment.Style, segment.LetterSpacing);
                if (segment.Width > 0f && take == segment.Length && MathF.Abs(segment.Width - metrics.Width) > 0.01f)
                {
                    width += segment.Width;
                }
                else
                {
                    width += metrics.GetWidth(take);
                }
                remaining -= take;
            }
        }

        return width;
    }

    private IEnumerable<LineSegment> EnumerateSegments(LayoutLine line)
    {
        return EnumerateSegments(line.Runs, line.Images, line.Shapes, line.Charts, line.Equations);
    }

    private IEnumerable<LineSegment> EnumerateSegments(IReadOnlyList<LayoutRun> runs, IReadOnlyList<LayoutImage> images, IReadOnlyList<LayoutShape> shapes, IReadOnlyList<LayoutChart> charts, IReadOnlyList<LayoutEquation> equations)
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
                segments.Add((run.X, LineSegment.CreateText(run.Text, run.Style, run.Width, run.Length, run.LetterSpacing)));
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

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Vibe.Word.Editor.Editing;

using Vibe.Office.Editing;

namespace Vibe.Word.Editor;

public sealed class EditorController : IEditorMutableSession, IContentControlInteractionSession, IEditorLayoutRefreshService
{
    private readonly EditorLayoutService _layoutService;
    private readonly EditorSelectionService _selectionService;

    public Document Document { get; }
    public LayoutSettings LayoutSettings => _layoutService.Settings;
    public DocumentLayout Layout => _layoutService.Layout;
    public TextPosition Caret => _selectionService.Caret;
    public TextRange Selection => _selectionService.Selection;
    public IReadOnlyList<TextRange> SelectionRanges => _selectionService.SelectionRanges;
    public IReadOnlyList<TableSelectionRange> TableSelections => _selectionService.TableSelections;
    public Guid? SelectedFloatingObjectId => _selectionService.SelectedFloatingObjectId;
    public IReadOnlyList<Guid> SelectedFloatingObjectIds => _selectionService.SelectedFloatingObjectIds;
    public IReadOnlyList<int> DirtyPages { get; private set; } = Array.Empty<int>();
    public long DirtyVersion { get; private set; }

    public event EventHandler? Changed;

    public EditorController(ITextMeasurer measurer, Document? document = null)
    {
        var textMeasurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
        Document = document ?? new Document();
        _layoutService = new EditorLayoutService(Document, textMeasurer);
        _selectionService = new EditorSelectionService(Document, _layoutService, textMeasurer);
        _layoutService.LayoutChanged += OnLayoutChanged;
        _selectionService.SelectionChanged += OnSelectionChanged;
        DirtyPages = _layoutService.Layout.Pages.Count == 0 ? Array.Empty<int>() : Enumerable.Range(0, _layoutService.Layout.Pages.Count).ToArray();
        DirtyVersion = 1;
    }

    public void UpdateLayout(float viewportWidth, float viewportHeight)
    {
        _layoutService.UpdateViewport(viewportWidth, viewportHeight);
    }

    public void RefreshLayout()
    {
        _layoutService.RefreshLayout(null);
    }

    public void RefreshLayout(int? dirtyParagraphIndex)
    {
        _layoutService.RefreshLayout(dirtyParagraphIndex);
    }

    public bool TryGetCaretPoint(out DocPoint point, out int lineIndex)
    {
        return _selectionService.TryGetCaretPoint(out point, out lineIndex);
    }

    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        DeleteSelectionIfAny();

        var paragraph = this.GetParagraphFast(Caret.ParagraphIndex);
        if (Document.TrackChangesEnabled)
        {
            InsertTextWithRevision(paragraph, Caret.Offset, text);
        }
        else
        {
            InsertTextAtPosition(paragraph, Caret.Offset, text);
        }
        _selectionService.SetCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset + text.Length), false);
        Reflow(dirtyParagraphIndex);
    }

    void IEditorMutableSession.InsertText(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        InsertText(text.ToString());
    }

    public void InsertEquation(MathElement root, TextStyleProperties? style = null, string? styleId = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        DeleteSelectionIfAny();

        var paragraph = this.GetParagraphFast(Caret.ParagraphIndex);
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

        if (Document.TrackChangesEnabled)
        {
            InsertInlineWithRevision(paragraph, Caret.Offset, equation);
        }
        else
        {
            InsertInlineAtPosition(paragraph, Caret.Offset, equation);
        }
        _selectionService.SetCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset + 1), false);
        Reflow(dirtyParagraphIndex);
    }

    public void InsertParagraphBreak()
    {
        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        DeleteSelectionIfAny();

        var location = Document.GetParagraphLocation(Caret.ParagraphIndex);
        var paragraph = location.Paragraph;
        var offset = Caret.Offset;
        var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
        var newStyleId = ResolveNextParagraphStyleId(paragraph, offset >= paragraphLength);
        var moveCaretToNextParagraph = offset > 0 || paragraphLength == 0;
        var newParagraph = new ParagraphBlock(string.Empty, paragraph.ListInfo?.Clone())
        {
            StyleId = newStyleId
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

        Reflow(dirtyParagraphIndex);
        var targetIndex = moveCaretToNextParagraph ? Caret.ParagraphIndex + 1 : Caret.ParagraphIndex;
        _selectionService.SetCaret(new TextPosition(targetIndex, 0), false);
    }

    private string? ResolveNextParagraphStyleId(ParagraphBlock paragraph, bool isAtEnd)
    {
        if (!isAtEnd)
        {
            return paragraph.StyleId;
        }

        var currentStyleId = paragraph.StyleId ?? Document.Styles.DefaultParagraphStyleId;
        if (string.IsNullOrWhiteSpace(currentStyleId))
        {
            return paragraph.StyleId;
        }

        if (!Document.Styles.ParagraphStyles.TryGetValue(currentStyleId, out var style))
        {
            return paragraph.StyleId;
        }

        var nextStyleId = style.NextStyleId;
        if (string.IsNullOrWhiteSpace(nextStyleId))
        {
            return paragraph.StyleId;
        }

        return Document.Styles.ParagraphStyles.ContainsKey(nextStyleId)
            ? nextStyleId
            : paragraph.StyleId;
    }

    public void InsertInline(Inline inline)
    {
        ArgumentNullException.ThrowIfNull(inline);

        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        DeleteSelectionIfAny();

        var paragraph = this.GetParagraphFast(Caret.ParagraphIndex);
        var offset = Caret.Offset;
        if (Document.TrackChangesEnabled)
        {
            InsertInlineWithRevision(paragraph, offset, inline);
        }
        else
        {
            InsertInlineAtPosition(paragraph, offset, inline);
        }
        var length = DocumentEditHelpers.GetInlineLength(inline);
        _selectionService.SetCaret(new TextPosition(Caret.ParagraphIndex, offset + length), false);
        Reflow(dirtyParagraphIndex);
    }

    public void InsertInlines(IReadOnlyList<Inline> inlines)
    {
        if (inlines is null || inlines.Count == 0)
        {
            return;
        }

        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        DeleteSelectionIfAny();

        var paragraph = this.GetParagraphFast(Caret.ParagraphIndex);
        var offset = Caret.Offset;
        var totalLength = GetInlineRangeLength(inlines);
        if (Document.TrackChangesEnabled)
        {
            InsertInlineRangeWithRevision(paragraph, offset, inlines, totalLength);
        }
        else
        {
            InsertInlineRangeAtPosition(paragraph, offset, inlines, totalLength);
        }
        _selectionService.SetCaret(new TextPosition(Caret.ParagraphIndex, offset + totalLength), false);
        Reflow(dirtyParagraphIndex);
    }

    public void InsertBlock(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        DeleteSelectionIfAny();

        var location = Document.GetParagraphLocation(Caret.ParagraphIndex);
        if (location.IsInTable)
        {
            var insertIndex = Math.Clamp(location.BlockIndex + 1, 0, Document.Blocks.Count);
            var newParagraph = new ParagraphBlock();
            Document.Blocks.Insert(insertIndex, block);
            Document.Blocks.Insert(insertIndex + 1, newParagraph);
            Reflow(dirtyParagraphIndex);
            _selectionService.SetCaret(new TextPosition(FindParagraphIndex(newParagraph), 0), false);
            return;
        }

        var paragraph = location.Paragraph;
        var offset = Caret.Offset;
        var nextParagraph = new ParagraphBlock(string.Empty, paragraph.ListInfo?.Clone())
        {
            StyleId = paragraph.StyleId
        };
        CopyParagraphProperties(paragraph.Properties, nextParagraph.Properties);

        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            var before = text.Substring(0, offset);
            var after = text.Substring(offset);
            paragraph.Text = before;
            nextParagraph.Text = after;
        }
        else
        {
            SplitInlinesAtOffset(paragraph, offset, out var before, out var after);
            paragraph.Inlines.Clear();
            paragraph.Inlines.AddRange(before);
            NormalizeInlines(paragraph);

            nextParagraph.Inlines.AddRange(after);
            NormalizeInlines(nextParagraph);
        }

        SplitFloatingAnchors(paragraph, nextParagraph, offset);
        var blockInsertIndex = Math.Clamp(location.BlockIndex + 1, 0, Document.Blocks.Count);
        Document.Blocks.Insert(blockInsertIndex, block);
        Document.Blocks.Insert(blockInsertIndex + 1, nextParagraph);

        Reflow(dirtyParagraphIndex);
        _selectionService.SetCaret(new TextPosition(FindParagraphIndex(nextParagraph), 0), false);
    }

    public void Backspace()
    {
        var dirtyParagraphIndex = GetDirtyParagraphIndex();
        if (DeleteSelectionIfAny())
        {
            Reflow(dirtyParagraphIndex);
            return;
        }

        if (Document.TrackChangesEnabled)
        {
            if (Caret.Offset > 0)
            {
                var paragraph = this.GetParagraphFast(Caret.ParagraphIndex);
                MarkDeletionRange(paragraph, Caret.Offset - 1, Caret.Offset);
                _selectionService.SetCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset - 1), false);
                Reflow(dirtyParagraphIndex);
                return;
            }

            if (Caret.ParagraphIndex > 0)
            {
                var paragraph = this.GetParagraphFast(Caret.ParagraphIndex);
                InsertDeletionMarker(paragraph, 0);
                Reflow(dirtyParagraphIndex);
            }

            return;
        }

        if (Caret.Offset > 0)
        {
            var paragraph = this.GetParagraphFast(Caret.ParagraphIndex);
            DeleteRangeInParagraph(paragraph, Caret.Offset - 1, Caret.Offset);
            _selectionService.SetCaret(new TextPosition(Caret.ParagraphIndex, Caret.Offset - 1), false);
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
                _selectionService.SetCaret(new TextPosition(Caret.ParagraphIndex - 1, DocumentEditHelpers.GetParagraphLength(previous)), false);
                return;
            }

            var current = currentLocation.Paragraph;
            var newOffset = DocumentEditHelpers.GetParagraphLength(previous);
            AppendParagraphContent(previous, current);
            Document.RemoveParagraphAt(currentLocation);
            _selectionService.SetCaret(new TextPosition(Caret.ParagraphIndex - 1, newOffset), false);
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
        if (Document.TrackChangesEnabled)
        {
            if (Caret.Offset < DocumentEditHelpers.GetParagraphLength(paragraph))
            {
                MarkDeletionRange(paragraph, Caret.Offset, Caret.Offset + 1);
                Reflow(dirtyParagraphIndex);
            }
            else if (Caret.ParagraphIndex < this.GetParagraphCountFast() - 1)
            {
                InsertDeletionMarker(paragraph, Caret.Offset);
                Reflow(dirtyParagraphIndex);
            }

            return;
        }

        if (Caret.Offset < DocumentEditHelpers.GetParagraphLength(paragraph))
        {
            DeleteRangeInParagraph(paragraph, Caret.Offset, Caret.Offset + 1);
            Reflow(dirtyParagraphIndex);
            return;
        }

        if (Caret.ParagraphIndex < this.GetParagraphCountFast() - 1)
        {
            var nextLocation = Document.GetParagraphLocation(Caret.ParagraphIndex + 1);
            if (!currentLocation.IsSameContainer(nextLocation))
            {
                _selectionService.SetCaret(new TextPosition(Caret.ParagraphIndex + 1, 0), false);
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
        _selectionService.MoveLeft(extendSelection);
    }

    public void MoveRight(bool extendSelection)
    {
        _selectionService.MoveRight(extendSelection);
    }

    public void MoveUp(bool extendSelection)
    {
        _selectionService.MoveUp(extendSelection);
    }

    public void MoveDown(bool extendSelection)
    {
        _selectionService.MoveDown(extendSelection);
    }

    public void SetCaretFromPoint(float x, float y, bool extendSelection)
    {
        var mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        _selectionService.SetCaretFromPoint(x, y, mode);
    }

    public void SetCaretFromPoint(float x, float y, SelectionUpdateMode mode)
    {
        _selectionService.SetCaretFromPoint(x, y, mode);
    }

    public void SetSelection(TextRange selection)
    {
        _selectionService.SetSelection(selection, SelectionUpdateMode.Replace);
    }

    public void SetSelection(TextRange selection, SelectionUpdateMode mode)
    {
        _selectionService.SetSelection(selection, mode);
    }

    public bool TrySelectFirstFloatingObject()
    {
        return _selectionService.TrySelectFirstFloatingObject();
    }

    public EquationInline? GetEquationAtCaret()
    {
        return _selectionService.GetEquationAtCaret();
    }

    public EquationInline? GetEquationAtPosition(TextPosition position)
    {
        return _selectionService.GetEquationAtPosition(position);
    }

    public bool TryGetContentControlAtPoint(float x, float y, out ContentControlHit hit)
    {
        return _selectionService.TryGetContentControlAtPoint(x, y, out hit);
    }

    public bool TryGetContentControlAtCaret(out ContentControlHit hit)
    {
        return _selectionService.TryGetContentControlAtCaret(out hit);
    }

    public bool TryActivateContentControl(
        in ContentControlHit hit,
        ContentControlActivationSource source,
        EditorModifiers modifiers,
        IContentControlInteractionService? interactionService)
    {
        _selectionService.SetCaret(hit.Position, SelectionUpdateMode.Replace);
        if (DocumentEditHelpers.IsContentControlContentLocked(hit.Properties))
        {
            return false;
        }

        var updated = hit.Properties.DataType switch
        {
            ContentControlDataType.CheckBox => TryToggleCheckBox(hit.Properties),
            ContentControlDataType.DropDownList => TrySelectListItem(hit.Properties, allowCustom: false, modifiers, interactionService),
            ContentControlDataType.ComboBox => TrySelectListItem(hit.Properties, allowCustom: true, modifiers, interactionService),
            ContentControlDataType.Date => TrySelectDate(hit.Properties, interactionService),
            _ => false
        };

        if (updated)
        {
            Reflow(hit.Position.ParagraphIndex);
        }

        return updated;
    }
    private void Reflow(int? dirtyParagraphIndex)
    {
        _layoutService.RefreshLayout(dirtyParagraphIndex);
    }

    private bool TryToggleCheckBox(ContentControlProperties properties)
    {
        var newValue = !properties.IsChecked.GetValueOrDefault();
        var bindingValue = newValue ? "true" : "false";
        if (!TryUpdateContentControlBinding(properties, bindingValue))
        {
            return false;
        }

        properties.IsChecked = newValue;
        properties.ShowingPlaceholder = false;
        return true;
    }

    private bool TrySelectListItem(
        ContentControlProperties properties,
        bool allowCustom,
        EditorModifiers modifiers,
        IContentControlInteractionService? interactionService)
    {
        string? selectedValue = null;
        if (interactionService is not null)
        {
            var currentValue = properties.SelectedValue;
            if (!interactionService.TryPickListItem(properties, properties.Items, currentValue, allowCustom, out selectedValue))
            {
                return false;
            }

            if (!allowCustom && !TryResolveListSelection(properties, selectedValue, out selectedValue))
            {
                return false;
            }
        }
        else if (!TrySelectNextListItem(properties, modifiers, out selectedValue))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(selectedValue))
        {
            return false;
        }

        if (!TryUpdateContentControlBinding(properties, selectedValue))
        {
            return false;
        }

        properties.SelectedValue = selectedValue;
        properties.ShowingPlaceholder = false;
        return true;
    }

    private bool TrySelectDate(ContentControlProperties properties, IContentControlInteractionService? interactionService)
    {
        DateTimeOffset selectedDate;
        if (interactionService is not null)
        {
            var currentDate = ResolveCurrentDate(properties);
            if (!interactionService.TryPickDate(properties, currentDate, out selectedDate))
            {
                return false;
            }
        }
        else
        {
            selectedDate = DateTimeOffset.Now;
        }

        var value = selectedDate.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        if (!TryUpdateContentControlBinding(properties, value))
        {
            return false;
        }

        properties.FullDate = value;
        properties.ShowingPlaceholder = false;
        return true;
    }

    private bool TryUpdateContentControlBinding(ContentControlProperties properties, string value)
    {
        if (properties.DataBinding is null)
        {
            return true;
        }

        return ContentControlValueResolver.TryUpdateContentControlBinding(properties.DataBinding, Document, value);
    }

    private DateTimeOffset? ResolveCurrentDate(ContentControlProperties properties)
    {
        if (!string.IsNullOrWhiteSpace(properties.FullDate)
            && DateTimeOffset.TryParse(properties.FullDate, out var parsed))
        {
            return parsed;
        }

        if (properties.DataBinding is not null
            && ContentControlValueResolver.TryResolveContentControlBinding(properties.DataBinding, Document, out var bindingValue)
            && DateTimeOffset.TryParse(bindingValue, out var boundDate))
        {
            return boundDate;
        }

        return null;
    }

    private static bool TrySelectNextListItem(
        ContentControlProperties properties,
        EditorModifiers modifiers,
        out string? selectedValue)
    {
        selectedValue = null;
        if (properties.Items.Count == 0)
        {
            return false;
        }

        var direction = (modifiers & EditorModifiers.Shift) != 0 ? -1 : 1;
        var startIndex = -1;
        if (!string.IsNullOrWhiteSpace(properties.SelectedValue))
        {
            for (var i = 0; i < properties.Items.Count; i++)
            {
                var itemValue = properties.Items[i].Value;
                if (string.Equals(itemValue, properties.SelectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    startIndex = i;
                    break;
                }
            }
        }

        for (var i = 0; i < properties.Items.Count; i++)
        {
            var index = direction > 0
                ? (startIndex + 1 + i) % properties.Items.Count
                : (startIndex - 1 - i + properties.Items.Count * 2) % properties.Items.Count;
            var candidate = properties.Items[index];
            selectedValue = candidate.Value ?? candidate.DisplayText;
            if (!string.IsNullOrWhiteSpace(selectedValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveListSelection(
        ContentControlProperties properties,
        string? input,
        out string? selectedValue)
    {
        selectedValue = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        for (var i = 0; i < properties.Items.Count; i++)
        {
            var item = properties.Items[i];
            if (string.Equals(item.Value, input, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.DisplayText, input, StringComparison.OrdinalIgnoreCase))
            {
                selectedValue = item.Value ?? item.DisplayText;
                return !string.IsNullOrWhiteSpace(selectedValue);
            }
        }

        if (int.TryParse(input, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index)
            && index > 0 && index <= properties.Items.Count)
        {
            var item = properties.Items[index - 1];
            selectedValue = item.Value ?? item.DisplayText;
            return !string.IsNullOrWhiteSpace(selectedValue);
        }

        return false;
    }

    private bool DeleteSelectionIfAny()
    {
        if (Selection.IsEmpty)
        {
            return false;
        }

        var range = Selection.Normalize();
        if (Document.TrackChangesEnabled)
        {
            if (ApplyDeletionRevision(range))
            {
                _selectionService.SetCaret(new TextPosition(range.Start.ParagraphIndex, range.Start.Offset), false);
                return true;
            }
        }

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
            var startLength = DocumentEditHelpers.GetParagraphLength(startParagraph);
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

        _selectionService.SetCaret(new TextPosition(range.Start.ParagraphIndex, range.Start.Offset), false);
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

    private void InsertTextWithRevision(ParagraphBlock paragraph, int offset, string text)
    {
        var revision = EditorRevisionHelper.CreateRevision(Document, RevisionKind.Insert);
        if (revision is null)
        {
            InsertTextAtPosition(paragraph, offset, text);
            return;
        }

        EnsureParagraphInlines(paragraph);
        var position = FindInlinePosition(paragraph, offset);
        var run = CreateInsertionRun(paragraph, position, text);
        var inlines = new List<Inline>(3)
        {
            new RevisionStartInline(revision),
            run,
            new RevisionEndInline(revision.Kind, revision.Id)
        };

        InsertInlineRangeAtPosition(paragraph, offset, inlines, text.Length);
    }

    private void InsertInlineAtPosition(ParagraphBlock paragraph, int offset, Inline inline)
    {
        EnsureParagraphInlines(paragraph);
        var inlineLength = DocumentEditHelpers.GetInlineLength(inline);
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

    private void InsertInlineWithRevision(ParagraphBlock paragraph, int offset, Inline inline)
    {
        var revision = EditorRevisionHelper.CreateRevision(Document, RevisionKind.Insert);
        if (revision is null)
        {
            InsertInlineAtPosition(paragraph, offset, inline);
            return;
        }

        var inlines = new List<Inline>(3)
        {
            new RevisionStartInline(revision),
            inline,
            new RevisionEndInline(revision.Kind, revision.Id)
        };

        InsertInlineRangeAtPosition(
            paragraph,
            offset,
            inlines,
            DocumentEditHelpers.GetInlineLength(inline));
    }

    private void InsertInlineRangeAtPosition(
        ParagraphBlock paragraph,
        int offset,
        IReadOnlyList<Inline> inlines,
        int totalLength)
    {
        EnsureParagraphInlines(paragraph);
        if (totalLength > 0)
        {
            ShiftFloatingAnchorsOnInsert(paragraph, offset, totalLength);
        }

        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Inlines.AddRange(inlines);
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
                paragraph.Inlines.InsertRange(position.Index, inlines);
            }
            else if (insertAt >= run.Text.Length)
            {
                paragraph.Inlines.InsertRange(position.Index + 1, inlines);
            }
            else
            {
                var beforeText = run.Text.SliceBuffer(0, insertAt);
                var afterText = run.Text.SliceBuffer(insertAt, run.Text.Length - insertAt);
                var beforeRun = new RunInline(beforeText, run.Style) { StyleId = run.StyleId };
                var afterRun = new RunInline(afterText, run.Style) { StyleId = run.StyleId };
                paragraph.Inlines.RemoveAt(position.Index);
                paragraph.Inlines.Insert(position.Index, beforeRun);
                paragraph.Inlines.InsertRange(position.Index + 1, inlines);
                paragraph.Inlines.Insert(position.Index + 1 + inlines.Count, afterRun);
            }
        }
        else
        {
            var insertIndex = position.OffsetInInline <= 0 ? position.Index : position.Index + 1;
            paragraph.Inlines.InsertRange(insertIndex, inlines);
        }

        NormalizeInlines(paragraph);
    }

    private void InsertInlineRangeWithRevision(
        ParagraphBlock paragraph,
        int offset,
        IReadOnlyList<Inline> inlines,
        int totalLength)
    {
        var revision = EditorRevisionHelper.CreateRevision(Document, RevisionKind.Insert);
        if (revision is null)
        {
            InsertInlineRangeAtPosition(paragraph, offset, inlines, totalLength);
            return;
        }

        var wrapped = new List<Inline>(inlines.Count + 2)
        {
            new RevisionStartInline(revision)
        };
        wrapped.AddRange(inlines);
        wrapped.Add(new RevisionEndInline(revision.Kind, revision.Id));

        InsertInlineRangeAtPosition(paragraph, offset, wrapped, totalLength);
    }

    private static int GetInlineRangeLength(IReadOnlyList<Inline> inlines)
    {
        var length = 0;
        foreach (var inline in inlines)
        {
            length += DocumentEditHelpers.GetInlineLength(inline);
        }

        return length;
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
                case TotalPagesInline:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
                case MetadataStartInline:
                case MetadataEndInline:
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
                case RevisionStartInline:
                case RevisionEndInline:
                case RevisionRangeStartInline:
                case RevisionRangeEndInline:
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
            var length = DocumentEditHelpers.GetInlineLength(inlines[i]);
            var end = position + length;
            if (offset <= end)
            {
                return new InlinePosition(i, Math.Max(0, offset - position), length);
            }

            position = end;
        }

        var lastIndex = inlines.Count - 1;
        var lastLength = DocumentEditHelpers.GetInlineLength(inlines[lastIndex]);
        return new InlinePosition(lastIndex, lastLength, lastLength);
    }

    private static RunInline CreateInsertionRun(ParagraphBlock paragraph, InlinePosition position, string text)
    {
        TextStyleProperties? style = null;
        string? styleId = null;
        HyperlinkInfo? hyperlink = null;

        if (paragraph.Inlines.Count > 0)
        {
            var current = paragraph.Inlines[position.Index];
            if (current is RunInline run && run.Text.Length > 0)
            {
                style = run.Style?.Clone();
                styleId = run.StyleId;
                hyperlink = run.Hyperlink;
            }
            else
            {
                var adjacent = GetAdjacentRunFormatting(paragraph, position.OffsetInInline <= 0 ? position.Index : position.Index + 1);
                style = adjacent.Style;
                styleId = adjacent.StyleId;
                hyperlink = adjacent.Hyperlink;
            }
        }

        return new RunInline(text, style)
        {
            StyleId = styleId,
            Hyperlink = hyperlink
        };
    }

    private int FindParagraphIndex(ParagraphBlock paragraph)
    {
        var count = 0;
        for (var i = 0; i < Document.Blocks.Count; i++)
        {
            switch (Document.Blocks[i])
            {
                case ParagraphBlock candidate:
                    if (ReferenceEquals(candidate, paragraph))
                    {
                        return count;
                    }

                    count++;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var candidate in cell.Paragraphs)
                            {
                                if (ReferenceEquals(candidate, paragraph))
                                {
                                    return count;
                                }

                                count++;
                            }
                        }
                    }

                    break;
            }
        }

        return Math.Clamp(Caret.ParagraphIndex, 0, Math.Max(0, this.GetParagraphCountFast() - 1));
    }

    private static void SplitInlinesAtOffset(ParagraphBlock paragraph, int offset, out List<Inline> before, out List<Inline> after)
    {
        before = new List<Inline>();
        after = new List<Inline>();

        var length = DocumentEditHelpers.GetParagraphLength(paragraph);
        var splitOffset = Math.Clamp(offset, 0, length);
        var position = 0;

        foreach (var inline in paragraph.Inlines)
        {
            var inlineLength = DocumentEditHelpers.GetInlineLength(inline);
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
        var length = DocumentEditHelpers.GetParagraphLength(paragraph);
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
            var inlineLength = DocumentEditHelpers.GetInlineLength(inline);
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
        var targetLength = DocumentEditHelpers.GetParagraphLength(target);
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

    private static (TextStyleProperties? Style, string? StyleId, HyperlinkInfo? Hyperlink) GetAdjacentRunFormatting(
        ParagraphBlock paragraph,
        int insertIndex)
    {
        var inlines = paragraph.Inlines;
        for (var i = insertIndex - 1; i >= 0; i--)
        {
            if (inlines[i] is RunInline run && run.Text.Length > 0)
            {
                return (run.Style?.Clone(), run.StyleId, run.Hyperlink);
            }
        }

        for (var i = insertIndex; i < inlines.Count; i++)
        {
            if (inlines[i] is RunInline run && run.Text.Length > 0)
            {
                return (run.Style?.Clone(), run.StyleId, run.Hyperlink);
            }
        }

        return (null, null, null);
    }

    private bool ApplyDeletionRevision(TextRange range)
    {
        var revision = EditorRevisionHelper.CreateRevision(Document, RevisionKind.Delete);
        if (revision is null)
        {
            return false;
        }

        var startIndex = range.Start.ParagraphIndex;
        var endIndex = range.End.ParagraphIndex;
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = Document.GetParagraph(i);
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? range.Start.Offset : 0;
            var endOffset = i == endIndex ? range.End.Offset : paragraphLength;
            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);
            if (endOffset <= startOffset)
            {
                continue;
            }

            if (DocumentEditHelpers.WrapRangeWithRevisionMarkers(paragraph, startOffset, endOffset, revision))
            {
                NormalizeInlines(paragraph);
            }
        }

        return true;
    }

    private void MarkDeletionRange(ParagraphBlock paragraph, int startOffset, int endOffset)
    {
        var revision = EditorRevisionHelper.CreateRevision(Document, RevisionKind.Delete);
        if (revision is null)
        {
            DeleteRangeInParagraph(paragraph, startOffset, endOffset);
            return;
        }

        if (DocumentEditHelpers.WrapRangeWithRevisionMarkers(paragraph, startOffset, endOffset, revision))
        {
            NormalizeInlines(paragraph);
        }
    }

    private void InsertDeletionMarker(ParagraphBlock paragraph, int offset)
    {
        var revision = EditorRevisionHelper.CreateRevision(Document, RevisionKind.Delete);
        if (revision is null)
        {
            return;
        }

        var markers = new List<Inline>(2)
        {
            new RevisionStartInline(revision),
            new RevisionEndInline(revision.Kind, revision.Id)
        };

        InsertInlineRangeAtPosition(paragraph, offset, markers, 0);
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

    private void OnLayoutChanged(object? sender, LayoutChangedEventArgs e)
    {
        DirtyPages = e.DirtyPages;
        DirtyVersion++;
        OnChanged();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        DirtyPages = e.DirtyPages;
        DirtyVersion++;
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

using System.Globalization;
using System.Text;
using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public enum ClipboardPasteMode
{
    KeepSource,
    MatchDestination,
    TextOnly
}

public sealed class EditorClipboardController
{
    private readonly IEditorMutableSession _session;
    private readonly IClipboardService _clipboard;
    private readonly ITableSelectionSnapshotProvider? _tableSelectionProvider;

    public EditorClipboardController(
        IEditorMutableSession session,
        IClipboardService clipboard,
        ITableSelectionSnapshotProvider? tableSelectionProvider = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _tableSelectionProvider = tableSelectionProvider;
    }

    public bool CopySelection()
    {
        var content = BuildClipboardContent();
        if (content is null || content.Kind == ClipboardContentKind.None)
        {
            return false;
        }

        var text = BuildPlainText(content);
        if (!string.IsNullOrEmpty(text))
        {
            _clipboard.SetText(text);
        }

        _clipboard.SetContent(content);
        return true;
    }

    public bool TryBuildSelectionContent(out ClipboardContent content)
    {
        var built = BuildClipboardContent();
        if (built is null || built.Kind == ClipboardContentKind.None)
        {
            content = ClipboardContent.Empty();
            return false;
        }

        content = built;
        return true;
    }

    public bool CutSelection()
    {
        if (!CopySelection())
        {
            return false;
        }

        if (HasSelectedFloatingObjects())
        {
            if (DeleteSelectedFloatingObjects())
            {
                _session.RefreshLayout();
            }

            return true;
        }

        var selectionRanges = GetNormalizedSelectionRanges();
        if (selectionRanges.Length > 0)
        {
            DeleteSelectionRanges(selectionRanges);
        }

        return true;
    }

    public bool Paste(ClipboardPasteMode mode)
    {
        if (mode == ClipboardPasteMode.TextOnly)
        {
            return PasteTextOnly();
        }

        if (_clipboard.TryGetContent(out var content))
        {
            if (PasteContent(content, mode))
            {
                return true;
            }
        }

        return PasteTextOnly();
    }

    private ClipboardContent? BuildClipboardContent()
    {
        if (HasSelectedFloatingObjects()
            && TryCloneSelectedFloatingObjects(out var floatingObjects))
        {
            return ClipboardContent.FromFloatingObjects(floatingObjects);
        }

        var selectionRanges = GetNormalizedSelectionRanges();
        if (selectionRanges.Length == 0)
        {
            return null;
        }

        if (selectionRanges.Length == 1
            && _tableSelectionProvider is not null
            && _tableSelectionProvider.TryGetSnapshot(out var tableSnapshot))
        {
            var tableFragment = BuildTableFragment(tableSnapshot);
            return ClipboardContent.FromFragment(tableFragment);
        }

        var fragment = selectionRanges.Length == 1
            ? BuildSelectionFragment(selectionRanges[0])
            : BuildSelectionFragment(selectionRanges);
        return ClipboardContent.FromFragment(fragment);
    }

    private ClipboardDocumentFragment BuildSelectionFragment(TextRange range)
    {
        var fragment = new ClipboardDocumentFragment();
        var paragraphs = GetParagraphs();
        if (paragraphs.Count == 0)
        {
            return fragment;
        }

        AppendSelectionBlocks(fragment.Blocks, paragraphs, range);

        PopulateResources(_session.Document, fragment);
        return fragment;
    }

    private ClipboardDocumentFragment BuildSelectionFragment(IReadOnlyList<TextRange> ranges)
    {
        var fragment = new ClipboardDocumentFragment();
        if (ranges.Count == 0)
        {
            return fragment;
        }

        var paragraphs = GetParagraphs();
        if (paragraphs.Count == 0)
        {
            return fragment;
        }

        for (var i = 0; i < ranges.Count; i++)
        {
            AppendSelectionBlocks(fragment.Blocks, paragraphs, ranges[i]);
        }

        PopulateResources(_session.Document, fragment);
        return fragment;
    }

    private void AppendSelectionBlocks(List<Block> target, IReadOnlyList<ParagraphBlock> paragraphs, TextRange range)
    {
        var selection = range.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphs.Count - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphs.Count - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = paragraphs[i];
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? selection.Start.Offset : 0;
            var endOffset = i == endIndex ? selection.End.Offset : paragraphLength;

            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);

            if (startOffset <= 0 && endOffset >= paragraphLength)
            {
                target.Add(DocumentClone.CloneBlock(paragraph));
                continue;
            }

            var sliced = BuildParagraphFragment(paragraph, startOffset, endOffset);
            target.Add(sliced);
        }
    }

    private ClipboardDocumentFragment BuildTableFragment(EditorTableSelectionSnapshot snapshot)
    {
        var fragment = new ClipboardDocumentFragment();
        var table = BuildTableSelection(snapshot.Table, snapshot.RowStart, snapshot.RowEnd, snapshot.ColumnStart, snapshot.ColumnEnd);
        fragment.Blocks.Add(table);
        PopulateResources(_session.Document, fragment);
        return fragment;
    }

    private bool PasteContent(ClipboardContent content, ClipboardPasteMode mode)
    {
        switch (content.Kind)
        {
            case ClipboardContentKind.FloatingObject:
                if (content.FloatingObjects is null || content.FloatingObjects.Count == 0)
                {
                    return false;
                }

                return content.FloatingObjects.Count == 1
                    ? PasteFloatingObject(content.FloatingObjects[0])
                    : PasteFloatingObjects(content.FloatingObjects);
            case ClipboardContentKind.Blocks:
                if (content.Fragment is null || content.Fragment.Blocks.Count == 0)
                {
                    return false;
                }

                return PasteBlocksInternal(content.Fragment, mode);
            default:
                return false;
        }
    }

    private bool PasteFloatingObject(FloatingObject floating)
    {
        var document = _session.Document;
        if (document.ParagraphCount == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        if (!_session.Selection.IsEmpty)
        {
            _session.Backspace();
        }

        var paragraphIndex = Math.Clamp(_session.Caret.ParagraphIndex, 0, Math.Max(0, document.ParagraphCount - 1));
        var paragraph = document.GetParagraph(paragraphIndex);
        var offset = Math.Clamp(_session.Caret.Offset, 0, DocumentEditHelpers.GetParagraphLength(paragraph));
        var clone = DocumentClone.CloneFloatingObject(floating);
        clone.Anchor.AnchorOffset = offset;
        paragraph.FloatingObjects.Add(clone);
        _session.RefreshLayout();
        return true;
    }

    private bool PasteFloatingObjects(IReadOnlyList<FloatingObject> floatingObjects)
    {
        if (floatingObjects.Count == 0)
        {
            return false;
        }

        var document = _session.Document;
        if (document.ParagraphCount == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        if (!_session.Selection.IsEmpty)
        {
            _session.Backspace();
        }

        var paragraphIndex = Math.Clamp(_session.Caret.ParagraphIndex, 0, Math.Max(0, document.ParagraphCount - 1));
        var paragraph = document.GetParagraph(paragraphIndex);
        var offset = Math.Clamp(_session.Caret.Offset, 0, DocumentEditHelpers.GetParagraphLength(paragraph));

        for (var i = 0; i < floatingObjects.Count; i++)
        {
            var clone = DocumentClone.CloneFloatingObject(floatingObjects[i]);
            clone.Anchor.AnchorOffset = offset;
            paragraph.FloatingObjects.Add(clone);
        }

        _session.RefreshLayout();
        return true;
    }

    public bool PasteBlocks(ClipboardDocumentFragment fragment, ClipboardPasteMode mode)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        return PasteBlocksInternal(fragment, mode);
    }

    private bool PasteBlocksInternal(ClipboardDocumentFragment fragment, ClipboardPasteMode mode)
    {
        var blocks = CloneBlocks(fragment.Blocks);
        if (blocks.Count == 0)
        {
            return false;
        }

        if (mode == ClipboardPasteMode.MatchDestination)
        {
            var destination = ResolveDestinationFormatting();
            ApplyMatchDestinationFormatting(blocks, destination);
            ApplyResourcesMatchDestination(fragment.Resources, _session.Document, blocks, destination);
        }
        else
        {
            ApplyResources(fragment.Resources, _session.Document, blocks, mode);
        }

        InsertBlocksAtCaret(blocks);
        return true;
    }

    private bool PasteTextOnly()
    {
        if (!_clipboard.TryGetText(out var text))
        {
            return false;
        }

        return PasteText(text.AsSpan());
    }

    private bool PasteText(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        var inserted = false;
        var lineStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var value = text[i];
            if (value != '\r' && value != '\n')
            {
                continue;
            }

            var segment = text.Slice(lineStart, i - lineStart);
            if (!segment.IsEmpty)
            {
                _session.InsertText(segment);
                inserted = true;
            }

            _session.InsertParagraphBreak();
            inserted = true;

            if (value == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            lineStart = i + 1;
        }

        if (lineStart <= text.Length - 1)
        {
            var tail = text.Slice(lineStart);
            if (!tail.IsEmpty)
            {
                _session.InsertText(tail);
                inserted = true;
            }
        }

        return inserted;
    }

    private IReadOnlyList<ParagraphBlock> GetParagraphs()
    {
        var paragraphs = _session.Layout.Paragraphs;
        if (paragraphs.Count > 0)
        {
            return paragraphs;
        }

        return DocumentEditHelpers.BuildParagraphList(_session.Document);
    }

    private ParagraphBlock BuildParagraphFragment(ParagraphBlock paragraph, int startOffset, int endOffset)
    {
        var fragment = new ParagraphBlock(string.Empty, paragraph.ListInfo?.Clone())
        {
            StyleId = paragraph.StyleId
        };
        CopyParagraphProperties(paragraph.Properties, fragment.Properties);

        if (startOffset >= endOffset)
        {
            return fragment;
        }

        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            fragment.Text = SliceString(text, startOffset, endOffset - startOffset);
            return fragment;
        }

        var position = 0;
        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            var inlineStart = position;
            var inlineEnd = position + length;
            position = inlineEnd;

            if (inlineEnd <= startOffset || inlineStart >= endOffset)
            {
                continue;
            }

            if (inline is RunInline run)
            {
                var sliceStart = Math.Max(startOffset, inlineStart) - inlineStart;
                var sliceEnd = Math.Min(endOffset, inlineEnd) - inlineStart;
                var sliceLength = sliceEnd - sliceStart;
                if (sliceLength > 0)
                {
                    fragment.Inlines.Add(CloneRunSlice(run, sliceStart, sliceLength));
                }

                continue;
            }

            if (length > 0)
            {
                fragment.Inlines.Add(DocumentClone.CloneInline(inline));
            }
        }

        AppendFloatingObjects(paragraph, fragment, startOffset, endOffset);
        fragment.Text = DocumentEditHelpers.GetParagraphText(fragment);
        return fragment;
    }

    private static void AppendFloatingObjects(ParagraphBlock source, ParagraphBlock target, int startOffset, int endOffset)
    {
        if (source.FloatingObjects.Count == 0)
        {
            return;
        }

        foreach (var floating in source.FloatingObjects)
        {
            var anchor = floating.Anchor.AnchorOffset;
            if (anchor.HasValue)
            {
                if (anchor.Value < startOffset || anchor.Value > endOffset)
                {
                    continue;
                }
            }
            else if (startOffset > 0)
            {
                continue;
            }

            var clone = DocumentClone.CloneFloatingObject(floating);
            if (anchor.HasValue)
            {
                clone.Anchor.AnchorOffset = Math.Max(0, anchor.Value - startOffset);
            }

            target.FloatingObjects.Add(clone);
        }
    }

    private static RunInline CloneRunSlice(RunInline source, int start, int length)
    {
        var slice = source.Text.GetSlice(start, length);
        var clone = new RunInline(slice, source.Style?.Clone())
        {
            StyleId = source.StyleId,
            Hyperlink = CloneHyperlink(source.Hyperlink)
        };
        return clone;
    }

    private static HyperlinkInfo? CloneHyperlink(HyperlinkInfo? source)
    {
        if (source is null)
        {
            return null;
        }

        return new HyperlinkInfo(source.Uri, source.Anchor, source.Tooltip);
    }

    private static string SliceString(string text, int start, int length)
    {
        if (string.IsNullOrEmpty(text) || length <= 0)
        {
            return string.Empty;
        }

        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        if (length == 0)
        {
            return string.Empty;
        }

        return text.AsSpan(start, length).ToString();
    }

    private static TableBlock BuildTableSelection(TableBlock source, int rowStart, int rowEnd, int columnStart, int columnEnd)
    {
        var table = new TableBlock
        {
            StyleId = source.StyleId
        };

        var properties = source.Properties.Clone();
        if (properties.ColumnWidths.Count > 0)
        {
            var start = Math.Clamp(columnStart, 0, properties.ColumnWidths.Count - 1);
            var end = Math.Clamp(columnEnd, start, properties.ColumnWidths.Count - 1);
            var widths = new List<float>();
            for (var i = start; i <= end; i++)
            {
                widths.Add(properties.ColumnWidths[i]);
            }

            properties.ColumnWidths.Clear();
            properties.ColumnWidths.AddRange(widths);
        }

        CopyTableProperties(properties, table.Properties);

        var startRow = Math.Clamp(rowStart, 0, Math.Max(0, source.Rows.Count - 1));
        var endRow = Math.Clamp(rowEnd, startRow, Math.Max(0, source.Rows.Count - 1));
        for (var rowIndex = startRow; rowIndex <= endRow; rowIndex++)
        {
            var row = source.Rows[rowIndex];
            var clone = new TableRow
            {
                ContentControl = row.ContentControl?.Clone()
            };
            CopyTableRowProperties(row.Properties, clone.Properties);
            clone.Properties.GridBefore = null;
            clone.Properties.GridAfter = null;

            var columnIndex = Math.Max(0, row.Properties.GridBefore ?? 0);
            foreach (var cell in row.Cells)
            {
                var span = Math.Max(1, cell.ColumnSpan);
                var cellStart = columnIndex;
                var cellEnd = columnIndex + span - 1;
                columnIndex += span;

                if (cellEnd < columnStart)
                {
                    continue;
                }

                if (cellStart > columnEnd)
                {
                    break;
                }

                var intersectStart = Math.Max(cellStart, columnStart);
                var intersectEnd = Math.Min(cellEnd, columnEnd);
                var newSpan = Math.Max(1, intersectEnd - intersectStart + 1);

                var cellClone = CloneTableCell(cell);
                cellClone.ColumnSpan = newSpan;
                clone.Cells.Add(cellClone);
            }

            table.Rows.Add(clone);
        }

        return table;
    }

    private static TableCell CloneTableCell(TableCell source)
    {
        var clone = new TableCell
        {
            ContentControl = source.ContentControl?.Clone(),
            ColumnSpan = source.ColumnSpan,
            VerticalMerge = source.VerticalMerge
        };
        CopyTableCellProperties(source.Properties, clone.Properties);

        foreach (var paragraph in source.Paragraphs)
        {
            clone.Paragraphs.Add((ParagraphBlock)DocumentClone.CloneBlock(paragraph));
        }

        foreach (var metadata in source.Metadata)
        {
            clone.Metadata.Add(DocumentClone.CloneMetadataContainer(metadata));
        }

        return clone;
    }

    private void PopulateResources(Document document, ClipboardDocumentFragment fragment)
    {
        var paragraphStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var characterStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tableStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listIds = new HashSet<int>();
        var footnoteIds = new HashSet<int>();
        var endnoteIds = new HashSet<int>();
        var commentIds = new HashSet<int>();

        foreach (var block in fragment.Blocks)
        {
            CollectResourceUsage(block, paragraphStyles, characterStyles, tableStyles, listIds, footnoteIds, endnoteIds, commentIds);
        }

        ExpandStyleDependencies(document, paragraphStyles, characterStyles, tableStyles);

        if (paragraphStyles.Count > 0 || characterStyles.Count > 0 || tableStyles.Count > 0)
        {
            var styles = DocumentClone.CloneStyles(document.Styles);
            FilterStyles(styles, paragraphStyles, characterStyles, tableStyles);
            CopyStyles(styles, fragment.Resources.Styles);
        }

        if (document.Fonts.FontTable.Count > 0 || document.Fonts.Theme.HasValues)
        {
            var fonts = DocumentClone.CloneFonts(document.Fonts);
            CopyFonts(fonts, fragment.Resources.Fonts);
        }

        if (document.ThemeColors.HasValues)
        {
            var theme = DocumentClone.CloneThemeColors(document.ThemeColors);
            CopyThemeColors(theme, fragment.Resources.ThemeColors);
        }

        foreach (var listId in listIds)
        {
            if (document.ListDefinitions.TryGetValue(listId, out var definition))
            {
                fragment.Resources.ListDefinitions[listId] = definition.Clone();
            }
        }

        foreach (var footnoteId in footnoteIds)
        {
            if (document.Footnotes.TryGetValue(footnoteId, out var definition))
            {
                fragment.Resources.Footnotes[footnoteId] = DocumentClone.CloneFootnoteDefinition(definition);
            }
        }

        foreach (var endnoteId in endnoteIds)
        {
            if (document.Endnotes.TryGetValue(endnoteId, out var definition))
            {
                fragment.Resources.Endnotes[endnoteId] = DocumentClone.CloneEndnoteDefinition(definition);
            }
        }

        ExpandCommentIds(document.Comments, commentIds);
        foreach (var commentId in commentIds)
        {
            if (document.Comments.TryGetValue(commentId, out var definition))
            {
                fragment.Resources.Comments[commentId] = DocumentClone.CloneCommentDefinition(definition);
            }
        }
    }

    private static void CollectResourceUsage(
        Block block,
        HashSet<string> paragraphStyles,
        HashSet<string> characterStyles,
        HashSet<string> tableStyles,
        HashSet<int> listIds,
        HashSet<int> footnoteIds,
        HashSet<int> endnoteIds,
        HashSet<int> commentIds)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                CollectParagraphUsage(paragraph, paragraphStyles, characterStyles, listIds, footnoteIds, endnoteIds, commentIds);
                break;
            case TableBlock table:
                if (!string.IsNullOrWhiteSpace(table.StyleId))
                {
                    tableStyles.Add(table.StyleId);
                }

                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var paragraph in cell.Paragraphs)
                        {
                            CollectParagraphUsage(paragraph, paragraphStyles, characterStyles, listIds, footnoteIds, endnoteIds, commentIds);
                        }
                    }
                }

                break;
        }
    }

    private static void CollectParagraphUsage(
        ParagraphBlock paragraph,
        HashSet<string> paragraphStyles,
        HashSet<string> characterStyles,
        HashSet<int> listIds,
        HashSet<int> footnoteIds,
        HashSet<int> endnoteIds,
        HashSet<int> commentIds)
    {
        if (!string.IsNullOrWhiteSpace(paragraph.StyleId))
        {
            paragraphStyles.Add(paragraph.StyleId);
        }

        if (paragraph.ListInfo?.ListId is int listId)
        {
            listIds.Add(listId);
        }

        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case RunInline run:
                    if (!string.IsNullOrWhiteSpace(run.StyleId))
                    {
                        characterStyles.Add(run.StyleId);
                    }

                    break;
                case EquationInline equation:
                    if (!string.IsNullOrWhiteSpace(equation.StyleId))
                    {
                        characterStyles.Add(equation.StyleId);
                    }

                    break;
                case RubyInline ruby:
                    if (!string.IsNullOrWhiteSpace(ruby.BaseStyleId))
                    {
                        characterStyles.Add(ruby.BaseStyleId);
                    }

                    if (!string.IsNullOrWhiteSpace(ruby.RubyStyleId))
                    {
                        characterStyles.Add(ruby.RubyStyleId);
                    }

                    break;
                case FootnoteReferenceInline footnote:
                    footnoteIds.Add(footnote.Id);
                    if (!string.IsNullOrWhiteSpace(footnote.StyleId))
                    {
                        characterStyles.Add(footnote.StyleId);
                    }

                    break;
                case EndnoteReferenceInline endnote:
                    endnoteIds.Add(endnote.Id);
                    if (!string.IsNullOrWhiteSpace(endnote.StyleId))
                    {
                        characterStyles.Add(endnote.StyleId);
                    }

                    break;
                case CommentReferenceInline comment:
                    commentIds.Add(comment.Id);
                    if (!string.IsNullOrWhiteSpace(comment.StyleId))
                    {
                        characterStyles.Add(comment.StyleId);
                    }

                    break;
                case CommentRangeStartInline commentStart:
                    commentIds.Add(commentStart.Id);
                    break;
                case CommentRangeEndInline commentEnd:
                    commentIds.Add(commentEnd.Id);
                    break;
            }
        }
    }

    private static void ExpandStyleDependencies(
        Document document,
        HashSet<string> paragraphStyles,
        HashSet<string> characterStyles,
        HashSet<string> tableStyles)
    {
        var paragraphQueue = new Queue<string>(paragraphStyles);
        while (paragraphQueue.Count > 0)
        {
            var id = paragraphQueue.Dequeue();
            if (!document.Styles.ParagraphStyles.TryGetValue(id, out var style))
            {
                continue;
            }

            EnqueueStyleId(style.BasedOnId, paragraphStyles, paragraphQueue);
            EnqueueStyleId(style.NextStyleId, paragraphStyles, paragraphQueue);
            if (!string.IsNullOrWhiteSpace(style.LinkedStyleId))
            {
                if (characterStyles.Add(style.LinkedStyleId))
                {
                    // Keep queue for character styles separately.
                }
            }
        }

        var characterQueue = new Queue<string>(characterStyles);
        while (characterQueue.Count > 0)
        {
            var id = characterQueue.Dequeue();
            if (!document.Styles.CharacterStyles.TryGetValue(id, out var style))
            {
                continue;
            }

            EnqueueStyleId(style.BasedOnId, characterStyles, characterQueue);
            if (!string.IsNullOrWhiteSpace(style.LinkedStyleId))
            {
                paragraphStyles.Add(style.LinkedStyleId);
            }
        }

        var tableQueue = new Queue<string>(tableStyles);
        while (tableQueue.Count > 0)
        {
            var id = tableQueue.Dequeue();
            if (!document.Styles.TableStyles.TryGetValue(id, out var style))
            {
                continue;
            }

            EnqueueStyleId(style.BasedOnId, tableStyles, tableQueue);
            EnqueueStyleId(style.NextStyleId, tableStyles, tableQueue);
            if (!string.IsNullOrWhiteSpace(style.LinkedStyleId))
            {
                paragraphStyles.Add(style.LinkedStyleId);
            }
        }
    }

    private static void EnqueueStyleId(string? id, HashSet<string> set, Queue<string> queue)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        if (set.Add(id))
        {
            queue.Enqueue(id);
        }
    }

    private static void FilterStyles(
        DocumentStyles styles,
        HashSet<string> paragraphStyles,
        HashSet<string> characterStyles,
        HashSet<string> tableStyles)
    {
        if (paragraphStyles.Count == 0)
        {
            styles.ParagraphStyles.Clear();
        }
        else
        {
            RemoveUnused(styles.ParagraphStyles, paragraphStyles);
        }

        if (characterStyles.Count == 0)
        {
            styles.CharacterStyles.Clear();
        }
        else
        {
            RemoveUnused(styles.CharacterStyles, characterStyles);
        }

        if (tableStyles.Count == 0)
        {
            styles.TableStyles.Clear();
        }
        else
        {
            RemoveUnused(styles.TableStyles, tableStyles);
        }
    }

    private static void RemoveUnused<T>(Dictionary<string, T> styles, HashSet<string> usedIds)
    {
        var remove = new List<string>();
        foreach (var key in styles.Keys)
        {
            if (!usedIds.Contains(key))
            {
                remove.Add(key);
            }
        }

        foreach (var key in remove)
        {
            styles.Remove(key);
        }
    }

    private static void CopyStyles(DocumentStyles source, DocumentStyles target)
    {
        target.ParagraphStyles.Clear();
        foreach (var pair in source.ParagraphStyles)
        {
            target.ParagraphStyles[pair.Key] = pair.Value;
        }

        target.CharacterStyles.Clear();
        foreach (var pair in source.CharacterStyles)
        {
            target.CharacterStyles[pair.Key] = pair.Value;
        }

        target.TableStyles.Clear();
        foreach (var pair in source.TableStyles)
        {
            target.TableStyles[pair.Key] = pair.Value;
        }

        target.DefaultParagraphStyleId = source.DefaultParagraphStyleId;
        target.DefaultCharacterStyleId = source.DefaultCharacterStyleId;
        target.DefaultTableStyleId = source.DefaultTableStyleId;
    }

    private static void CopyFonts(DocumentFonts source, DocumentFonts target)
    {
        target.FontTable.Clear();
        foreach (var pair in source.FontTable)
        {
            target.FontTable[pair.Key] = pair.Value;
        }

        target.Theme.Clear();
        foreach (var pair in source.Theme.Entries)
        {
            target.Theme.Set(pair.Key, pair.Value);
        }
    }

    private static void CopyThemeColors(DocumentThemeColorMap source, DocumentThemeColorMap target)
    {
        target.Clear();
        foreach (var pair in source.Overrides)
        {
            target.Set(pair.Key, pair.Value);
        }
    }

    private static void ApplyResources(
        ClipboardResourceSet resources,
        Document target,
        IReadOnlyList<Block> blocks,
        ClipboardPasteMode mode)
    {
        MergeStyles(resources.Styles, target.Styles);
        MergeFonts(resources.Fonts, target.Fonts);
        MergeThemeColors(resources.ThemeColors, target.ThemeColors);

        var listIdMap = MergeListDefinitions(resources.ListDefinitions, target.ListDefinitions);
        var footnoteMap = MergeNotes(resources.Footnotes, target.Footnotes);
        var endnoteMap = MergeNotes(resources.Endnotes, target.Endnotes);
        var commentMap = MergeComments(resources.Comments, target.Comments);

        if (listIdMap.Count > 0 || footnoteMap.Count > 0 || endnoteMap.Count > 0 || commentMap.Count > 0)
        {
            RemapIds(blocks, listIdMap, footnoteMap, endnoteMap, commentMap);
        }
    }

    private static void ApplyResourcesMatchDestination(
        ClipboardResourceSet resources,
        Document target,
        IReadOnlyList<Block> blocks,
        MatchDestinationFormatting destination)
    {
        var listIdMap = destination.ListInfo is null
            ? MergeListDefinitions(resources.ListDefinitions, target.ListDefinitions)
            : new Dictionary<int, int>();
        var footnoteMap = MergeNotes(resources.Footnotes, target.Footnotes);
        var endnoteMap = MergeNotes(resources.Endnotes, target.Endnotes);
        var commentMap = MergeComments(resources.Comments, target.Comments);

        if (listIdMap.Count > 0 || footnoteMap.Count > 0 || endnoteMap.Count > 0 || commentMap.Count > 0)
        {
            RemapIds(blocks, listIdMap, footnoteMap, endnoteMap, commentMap);
        }
    }

    private MatchDestinationFormatting ResolveDestinationFormatting()
    {
        var document = _session.Document;
        var paragraphStyleId = document.Styles.DefaultParagraphStyleId;
        var characterStyleId = document.Styles.DefaultCharacterStyleId;
        var tableStyleId = document.Styles.DefaultTableStyleId;
        ListInfo? listInfo = null;

        if (document.ParagraphCount > 0)
        {
            var paragraphIndex = Math.Clamp(_session.Caret.ParagraphIndex, 0, Math.Max(0, document.ParagraphCount - 1));
            var location = document.GetParagraphLocation(paragraphIndex);
            var paragraph = location.Paragraph;

            if (!string.IsNullOrWhiteSpace(paragraph.StyleId)
                && document.Styles.ParagraphStyles.ContainsKey(paragraph.StyleId))
            {
                paragraphStyleId = paragraph.StyleId;
            }

            listInfo = paragraph.ListInfo?.Clone();
            characterStyleId = ResolveRunStyleIdAtCaret(paragraph, _session.Caret.Offset, characterStyleId, document);

            if (location.Table is { } table)
            {
                if (!string.IsNullOrWhiteSpace(table.StyleId)
                    && document.Styles.TableStyles.ContainsKey(table.StyleId))
                {
                    tableStyleId = table.StyleId;
                }
            }
        }

        return new MatchDestinationFormatting(paragraphStyleId, characterStyleId, listInfo, tableStyleId);
    }

    private static void ApplyMatchDestinationFormatting(IReadOnlyList<Block> blocks, MatchDestinationFormatting destination)
    {
        var applyListOverride = destination.ListInfo is not null;
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    ApplyMatchDestinationToParagraph(paragraph, destination, applyListOverride);
                    break;
                case TableBlock table:
                    table.StyleId = destination.TableStyleId;
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                ApplyMatchDestinationToParagraph(paragraph, destination, applyListOverride);
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static void ApplyMatchDestinationToParagraph(
        ParagraphBlock paragraph,
        MatchDestinationFormatting destination,
        bool applyListOverride)
    {
        paragraph.StyleId = destination.ParagraphStyleId;

        if (applyListOverride && destination.ListInfo is not null)
        {
            var sourceLevel = paragraph.ListInfo?.Level ?? 0;
            var mergedLevel = Math.Max(0, destination.ListInfo.Level + sourceLevel);
            paragraph.ListInfo = CloneListInfoWithLevel(destination.ListInfo, mergedLevel);
        }

        if (paragraph.Inlines.Count == 0)
        {
            return;
        }

        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case RunInline run:
                    run.StyleId = destination.CharacterStyleId;
                    run.Style = NormalizeTextStyleForMatchDestination(run.Style);
                    break;
                case EquationInline equation:
                    equation.StyleId = null;
                    equation.Style = NormalizeTextStyleForMatchDestination(equation.Style);
                    break;
                case RubyInline ruby:
                    ruby.BaseStyleId = null;
                    ruby.RubyStyleId = null;
                    ruby.BaseStyle = NormalizeTextStyleForMatchDestination(ruby.BaseStyle);
                    ruby.RubyStyle = NormalizeTextStyleForMatchDestination(ruby.RubyStyle);
                    break;
                case FootnoteReferenceInline footnote:
                    footnote.StyleId = destination.CharacterStyleId;
                    footnote.Style = NormalizeTextStyleForMatchDestination(footnote.Style);
                    break;
                case EndnoteReferenceInline endnote:
                    endnote.StyleId = destination.CharacterStyleId;
                    endnote.Style = NormalizeTextStyleForMatchDestination(endnote.Style);
                    break;
                case CommentReferenceInline comment:
                    comment.StyleId = destination.CharacterStyleId;
                    comment.Style = NormalizeTextStyleForMatchDestination(comment.Style);
                    break;
            }
        }
    }

    private static TextStyleProperties? NormalizeTextStyleForMatchDestination(TextStyleProperties? style)
    {
        if (style is null)
        {
            return null;
        }

        style.FontFamily = null;
        style.FontFamilyAscii = null;
        style.FontFamilyHighAnsi = null;
        style.FontFamilyEastAsia = null;
        style.FontFamilyComplexScript = null;
        style.FontSize = null;
        style.FontSizeComplexScript = null;
        style.ThemeFontAscii = null;
        style.ThemeFontHighAnsi = null;
        style.ThemeFontEastAsia = null;
        style.ThemeFontComplexScript = null;

        return style.HasValues ? style : null;
    }

    private static string? ResolveRunStyleIdAtCaret(
        ParagraphBlock paragraph,
        int offset,
        string? defaultId,
        Document document)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return ResolveCharacterStyleIdOrDefault(defaultId, document);
        }

        var position = 0;
        RunInline? lastRun = null;
        foreach (var inline in paragraph.Inlines)
        {
            var length = DocumentEditHelpers.GetInlineLength(inline);
            if (inline is RunInline run)
            {
                if (offset >= position && offset < position + length)
                {
                    return ResolveCharacterStyleIdOrDefault(run.StyleId ?? defaultId, document);
                }

                lastRun = run;
            }

            position += length;
        }

        return ResolveCharacterStyleIdOrDefault(lastRun?.StyleId ?? defaultId, document);
    }

    private static string? ResolveCharacterStyleIdOrDefault(string? styleId, Document document)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return document.Styles.DefaultCharacterStyleId;
        }

        return document.Styles.CharacterStyles.ContainsKey(styleId) ? styleId : document.Styles.DefaultCharacterStyleId;
    }

    private static ListInfo CloneListInfoWithLevel(ListInfo source, int level)
    {
        var clone = new ListInfo(source.Kind, level, source.ListId)
        {
            NumberFormat = source.NumberFormat,
            LevelText = source.LevelText,
            BulletSymbol = source.BulletSymbol,
            StartAt = source.StartAt,
            LeftIndent = source.LeftIndent,
            HangingIndent = source.HangingIndent,
            TabStop = source.TabStop
        };

        return clone;
    }

    private readonly record struct MatchDestinationFormatting(
        string? ParagraphStyleId,
        string? CharacterStyleId,
        ListInfo? ListInfo,
        string? TableStyleId);

    private static void MergeStyles(DocumentStyles source, DocumentStyles target)
    {
        foreach (var pair in source.ParagraphStyles)
        {
            if (!target.ParagraphStyles.ContainsKey(pair.Key))
            {
                target.ParagraphStyles[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in source.CharacterStyles)
        {
            if (!target.CharacterStyles.ContainsKey(pair.Key))
            {
                target.CharacterStyles[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in source.TableStyles)
        {
            if (!target.TableStyles.ContainsKey(pair.Key))
            {
                target.TableStyles[pair.Key] = pair.Value;
            }
        }
    }

    private static void MergeFonts(DocumentFonts source, DocumentFonts target)
    {
        foreach (var pair in source.FontTable)
        {
            if (!target.FontTable.ContainsKey(pair.Key))
            {
                target.FontTable[pair.Key] = pair.Value;
            }
        }
    }

    private static void MergeThemeColors(DocumentThemeColorMap source, DocumentThemeColorMap target)
    {
        if (target.HasValues)
        {
            return;
        }

        foreach (var pair in source.Overrides)
        {
            target.Set(pair.Key, pair.Value);
        }
    }

    private static Dictionary<int, int> MergeListDefinitions(
        IReadOnlyDictionary<int, ListDefinition> source,
        Dictionary<int, ListDefinition> target)
    {
        var map = new Dictionary<int, int>();
        if (source.Count == 0)
        {
            return map;
        }

        var nextId = target.Count == 0 ? 1 : target.Keys.Max() + 1;
        foreach (var pair in source)
        {
            var newId = pair.Key;
            if (target.ContainsKey(newId))
            {
                newId = nextId++;
                map[pair.Key] = newId;
            }

            target[newId] = CloneListDefinition(pair.Value, newId);
        }

        return map;
    }

    private static Dictionary<int, int> MergeNotes<T>(
        IReadOnlyDictionary<int, T> source,
        Dictionary<int, T> target)
        where T : class
    {
        var map = new Dictionary<int, int>();
        if (source.Count == 0)
        {
            return map;
        }

        var nextId = target.Count == 0 ? 1 : target.Keys.Max() + 1;
        foreach (var pair in source)
        {
            var newId = pair.Key;
            if (target.ContainsKey(newId))
            {
                newId = nextId++;
                map[pair.Key] = newId;
            }

            target[newId] = CloneNote(pair.Value, newId);
        }

        return map;
    }

    private static Dictionary<int, int> MergeComments(
        IReadOnlyDictionary<int, CommentDefinition> source,
        Dictionary<int, CommentDefinition> target)
    {
        var map = new Dictionary<int, int>();
        if (source.Count == 0)
        {
            return map;
        }

        var nextId = target.Count == 0 ? 1 : target.Keys.Max() + 1;
        foreach (var pair in source)
        {
            var newId = pair.Key;
            if (target.ContainsKey(newId))
            {
                newId = nextId++;
                map[pair.Key] = newId;
            }

            var clone = DocumentClone.CloneCommentDefinition(pair.Value);
            clone.Id = newId;
            target[newId] = clone;
        }

        if (map.Count > 0)
        {
            foreach (var pair in source)
            {
                var mappedId = map.TryGetValue(pair.Key, out var newId) ? newId : pair.Key;
                if (!target.TryGetValue(mappedId, out var clone))
                {
                    continue;
                }

                if (clone.ParentId.HasValue && map.TryGetValue(clone.ParentId.Value, out var parentId))
                {
                    clone.ParentId = parentId;
                }

                if (clone.ThreadId.HasValue && map.TryGetValue(clone.ThreadId.Value, out var threadId))
                {
                    clone.ThreadId = threadId;
                }
            }
        }

        return map;
    }

    private static T CloneNote<T>(T note, int newId) where T : class
    {
        switch (note)
        {
            case FootnoteDefinition footnote:
            {
                var clone = new FootnoteDefinition(newId);
                foreach (var block in footnote.Blocks)
                {
                    clone.Blocks.Add(DocumentClone.CloneBlock(block));
                }

                return clone as T ?? throw new InvalidOperationException();
            }
            case EndnoteDefinition endnote:
            {
                var clone = new EndnoteDefinition(newId);
                foreach (var block in endnote.Blocks)
                {
                    clone.Blocks.Add(DocumentClone.CloneBlock(block));
                }

                return clone as T ?? throw new InvalidOperationException();
            }
            case CommentDefinition comment:
            {
                var clone = new CommentDefinition(newId)
                {
                    Author = comment.Author,
                    Initials = comment.Initials,
                    Date = comment.Date,
                    ParentId = comment.ParentId,
                    ThreadId = comment.ThreadId,
                    IsResolved = comment.IsResolved,
                    ResolvedBy = comment.ResolvedBy,
                    ResolvedDate = comment.ResolvedDate
                };
                foreach (var block in comment.Blocks)
                {
                    clone.Blocks.Add(DocumentClone.CloneBlock(block));
                }

                return clone as T ?? throw new InvalidOperationException();
            }
            default:
                throw new InvalidOperationException("Unsupported note definition.");
        }
    }

    private static void ExpandCommentIds(IReadOnlyDictionary<int, CommentDefinition> comments, HashSet<int> commentIds)
    {
        if (commentIds.Count == 0 || comments.Count == 0)
        {
            return;
        }

        var threadIds = new HashSet<int>();
        foreach (var commentId in commentIds)
        {
            if (comments.TryGetValue(commentId, out var comment))
            {
                threadIds.Add(CommentThreading.ResolveThreadId(comment, comments));
            }
        }

        if (threadIds.Count == 0)
        {
            return;
        }

        foreach (var pair in comments)
        {
            var threadId = CommentThreading.ResolveThreadId(pair.Value, comments);
            if (threadIds.Contains(threadId))
            {
                commentIds.Add(pair.Key);
            }
        }
    }

    private static void RemapIds(
        IReadOnlyList<Block> blocks,
        Dictionary<int, int> listIdMap,
        Dictionary<int, int> footnoteMap,
        Dictionary<int, int> endnoteMap,
        Dictionary<int, int> commentMap)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    RemapParagraphIds(paragraph, listIdMap, footnoteMap, endnoteMap, commentMap);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                RemapParagraphIds(paragraph, listIdMap, footnoteMap, endnoteMap, commentMap);
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static void RemapParagraphIds(
        ParagraphBlock paragraph,
        Dictionary<int, int> listIdMap,
        Dictionary<int, int> footnoteMap,
        Dictionary<int, int> endnoteMap,
        Dictionary<int, int> commentMap)
    {
        if (paragraph.ListInfo?.ListId is int listId && listIdMap.TryGetValue(listId, out var newListId))
        {
            paragraph.ListInfo = CloneListInfoWithId(paragraph.ListInfo, newListId);
        }

        if (paragraph.Inlines.Count == 0)
        {
            return;
        }

        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            var inline = paragraph.Inlines[i];
            switch (inline)
            {
                case FootnoteReferenceInline footnote when footnoteMap.TryGetValue(footnote.Id, out var newFootnoteId):
                    paragraph.Inlines[i] = new FootnoteReferenceInline(newFootnoteId, footnote.Style?.Clone())
                    {
                        StyleId = footnote.StyleId
                    };
                    break;
                case EndnoteReferenceInline endnote when endnoteMap.TryGetValue(endnote.Id, out var newEndnoteId):
                    paragraph.Inlines[i] = new EndnoteReferenceInline(newEndnoteId, endnote.Style?.Clone())
                    {
                        StyleId = endnote.StyleId
                    };
                    break;
                case CommentReferenceInline comment when commentMap.TryGetValue(comment.Id, out var newCommentId):
                    paragraph.Inlines[i] = new CommentReferenceInline(newCommentId, comment.Style?.Clone())
                    {
                        StyleId = comment.StyleId
                    };
                    break;
                case CommentRangeStartInline commentStart when commentMap.TryGetValue(commentStart.Id, out var newRangeStartId):
                    paragraph.Inlines[i] = new CommentRangeStartInline(newRangeStartId);
                    break;
                case CommentRangeEndInline commentEnd when commentMap.TryGetValue(commentEnd.Id, out var newRangeEndId):
                    paragraph.Inlines[i] = new CommentRangeEndInline(newRangeEndId);
                    break;
            }
        }
    }

    private static void StripStyleReferences(IReadOnlyList<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    paragraph.StyleId = null;
                    paragraph.ListInfo = null;
                    foreach (var inline in paragraph.Inlines)
                    {
                        switch (inline)
                        {
                            case RunInline run:
                                run.StyleId = null;
                                break;
                            case EquationInline equation:
                                equation.StyleId = null;
                                break;
                            case RubyInline ruby:
                                ruby.BaseStyleId = null;
                                ruby.RubyStyleId = null;
                                break;
                            case FootnoteReferenceInline footnote:
                                footnote.StyleId = null;
                                break;
                            case EndnoteReferenceInline endnote:
                                endnote.StyleId = null;
                                break;
                            case CommentReferenceInline comment:
                                comment.StyleId = null;
                                break;
                        }
                    }

                    break;
                case TableBlock table:
                    table.StyleId = null;
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                paragraph.StyleId = null;
                                paragraph.ListInfo = null;
                                foreach (var inline in paragraph.Inlines)
                                {
                                    switch (inline)
                                    {
                                        case RunInline run:
                                            run.StyleId = null;
                                            break;
                                        case EquationInline equation:
                                            equation.StyleId = null;
                                            break;
                                        case RubyInline ruby:
                                            ruby.BaseStyleId = null;
                                            ruby.RubyStyleId = null;
                                            break;
                                        case FootnoteReferenceInline footnote:
                                            footnote.StyleId = null;
                                            break;
                                        case EndnoteReferenceInline endnote:
                                            endnote.StyleId = null;
                                            break;
                                        case CommentReferenceInline comment:
                                            comment.StyleId = null;
                                            break;
                                    }
                                }
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static ListInfo CloneListInfoWithId(ListInfo source, int newId)
    {
        var clone = new ListInfo(source.Kind, source.Level, newId)
        {
            NumberFormat = source.NumberFormat,
            LevelText = source.LevelText,
            BulletSymbol = source.BulletSymbol,
            StartAt = source.StartAt,
            LeftIndent = source.LeftIndent,
            HangingIndent = source.HangingIndent,
            TabStop = source.TabStop
        };

        return clone;
    }

    private static ListDefinition CloneListDefinition(ListDefinition source, int newId)
    {
        var clone = new ListDefinition(newId);
        foreach (var pair in source.Levels)
        {
            clone.Levels[pair.Key] = pair.Value.Clone();
        }

        return clone;
    }

    private static List<Block> CloneBlocks(IReadOnlyList<Block> blocks)
    {
        var clones = new List<Block>(blocks.Count);
        foreach (var block in blocks)
        {
            clones.Add(DocumentClone.CloneBlock(block));
        }

        return clones;
    }

    private void InsertBlocksAtCaret(IReadOnlyList<Block> blocks)
    {
        if (blocks.Count == 0)
        {
            return;
        }

        if (!_session.Selection.IsEmpty)
        {
            _session.Backspace();
        }

        var document = _session.Document;
        if (document.ParagraphCount == 0)
        {
            document.Blocks.Add(new ParagraphBlock());
        }

        var location = document.GetParagraphLocation(_session.Caret.ParagraphIndex);
        if (location.IsInTable)
        {
            InsertBlocksAfterTable(location, blocks);
            return;
        }

        var paragraph = location.Paragraph;
        var offset = _session.Caret.Offset;
        var nextParagraph = new ParagraphBlock(string.Empty, paragraph.ListInfo?.Clone())
        {
            StyleId = paragraph.StyleId
        };
        CopyParagraphProperties(paragraph.Properties, nextParagraph.Properties);

        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            var splitOffset = Math.Clamp(offset, 0, text.Length);
            paragraph.Text = text.Substring(0, splitOffset);
            nextParagraph.Text = text.Substring(splitOffset);
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
        var insertIndex = Math.Clamp(location.BlockIndex + 1, 0, document.Blocks.Count);
        document.Blocks.InsertRange(insertIndex, blocks);
        document.Blocks.Insert(insertIndex + blocks.Count, nextParagraph);

        var paragraphIndex = FindParagraphIndex(nextParagraph);
        _session.SetSelection(new TextRange(new TextPosition(paragraphIndex, 0), new TextPosition(paragraphIndex, 0)));
        _session.RefreshLayout();
    }

    private void InsertBlocksAfterTable(ParagraphLocation location, IReadOnlyList<Block> blocks)
    {
        var document = _session.Document;
        var insertIndex = Math.Clamp(location.BlockIndex + 1, 0, document.Blocks.Count);
        document.Blocks.InsertRange(insertIndex, blocks);

        var paragraph = FindFirstParagraphAfterIndex(document, insertIndex + blocks.Count);
        if (paragraph is null)
        {
            paragraph = new ParagraphBlock();
            document.Blocks.Insert(insertIndex + blocks.Count, paragraph);
        }

        var paragraphIndex = FindParagraphIndex(paragraph);
        _session.SetSelection(new TextRange(new TextPosition(paragraphIndex, 0), new TextPosition(paragraphIndex, 0)));
        _session.RefreshLayout();
    }

    private static ParagraphBlock? FindFirstParagraphAfterIndex(Document document, int blockIndex)
    {
        for (var i = Math.Max(0, blockIndex); i < document.Blocks.Count; i++)
        {
            switch (document.Blocks[i])
            {
                case ParagraphBlock paragraph:
                    return paragraph;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            if (cell.Paragraphs.Count > 0)
                            {
                                return cell.Paragraphs[0];
                            }
                        }
                    }

                    break;
            }
        }

        return null;
    }

    private int FindParagraphIndex(ParagraphBlock paragraph)
    {
        var count = 0;
        foreach (var block in _session.Document.Blocks)
        {
            switch (block)
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

        return Math.Clamp(_session.Caret.ParagraphIndex, 0, Math.Max(0, _session.Document.ParagraphCount - 1));
    }

    private static void SplitInlinesAtOffset(
        ParagraphBlock paragraph,
        int offset,
        out List<Inline> before,
        out List<Inline> after)
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
        paragraph.Text = DocumentEditHelpers.GetParagraphText(paragraph);
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

        return left.Style.IsEquivalentTo(right.Style);
    }

    private static void CopyParagraphProperties(ParagraphProperties source, ParagraphProperties target)
    {
        target.Alignment = source.Alignment;
        target.SpacingBefore = source.SpacingBefore;
        target.SpacingAfter = source.SpacingAfter;
        target.SpacingBeforeLines = source.SpacingBeforeLines;
        target.SpacingAfterLines = source.SpacingAfterLines;
        target.AutoSpacingBefore = source.AutoSpacingBefore;
        target.AutoSpacingAfter = source.AutoSpacingAfter;
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
        target.TextDirection = source.TextDirection;
        target.EastAsianLayout = source.EastAsianLayout?.Clone();
        target.ShadingColor = source.ShadingColor;
        target.SuppressLineNumbers = source.SuppressLineNumbers;
        target.DropCap = source.DropCap?.Clone();
        target.Frame = source.Frame?.Clone();
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
        target.TabStops.Clear();
        foreach (var tabStop in source.TabStops)
        {
            target.TabStops.Add(tabStop.Clone());
        }
    }

    private static void CopyTableProperties(TableProperties source, TableProperties target)
    {
        target.ColumnWidths.Clear();
        target.ColumnWidths.AddRange(source.ColumnWidths);
        target.Width = source.Width;
        target.WidthUnit = source.WidthUnit;
        target.Indent = source.Indent;
        target.IndentUnit = source.IndentUnit;
        target.Alignment = source.Alignment;
        target.LayoutMode = source.LayoutMode;
        target.CellSpacing = source.CellSpacing;
        target.CellSpacingUnit = source.CellSpacingUnit;
        target.CellPadding = source.CellPadding;
        target.ShadingColor = source.ShadingColor;
        target.Look = source.Look?.Clone();
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
        target.Borders.InsideHorizontal = source.Borders.InsideHorizontal?.Clone();
        target.Borders.InsideVertical = source.Borders.InsideVertical?.Clone();
    }

    private static void CopyTableRowProperties(TableRowProperties source, TableRowProperties target)
    {
        target.Height = source.Height;
        target.HeightRule = source.HeightRule;
        target.CantSplit = source.CantSplit;
        target.RepeatOnEachPage = source.RepeatOnEachPage;
        target.ShadingColor = source.ShadingColor;
        target.GridBefore = source.GridBefore;
        target.GridAfter = source.GridAfter;
    }

    private static void CopyTableCellProperties(TableCellProperties source, TableCellProperties target)
    {
        target.Padding = source.Padding;
        target.ShadingColor = source.ShadingColor;
        target.VerticalAlignment = source.VerticalAlignment;
        target.TextDirection = source.TextDirection;
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
    }

    private bool HasSelectedFloatingObjects()
    {
        return GetSelectedFloatingIds().Count > 0;
    }

    private IReadOnlyList<Guid> GetSelectedFloatingIds()
    {
        var ids = _session.SelectedFloatingObjectIds;
        if (ids.Count > 0)
        {
            return ids;
        }

        return _session.SelectedFloatingObjectId.HasValue
            ? new[] { _session.SelectedFloatingObjectId.Value }
            : Array.Empty<Guid>();
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

    private TextRange[] GetNormalizedSelectionRanges()
    {
        var ranges = _session.SelectionRanges;
        if (ranges.Count == 0)
        {
            return Array.Empty<TextRange>();
        }

        var list = new List<TextRange>(ranges.Count);
        for (var i = 0; i < ranges.Count; i++)
        {
            var normalized = ranges[i].Normalize();
            if (!normalized.IsEmpty)
            {
                list.Add(normalized);
            }
        }

        if (list.Count == 0)
        {
            return Array.Empty<TextRange>();
        }

        list.Sort(CompareRanges);

        var merged = new List<TextRange>(list.Count);
        var current = list[0];
        for (var i = 1; i < list.Count; i++)
        {
            var candidate = list[i];
            if (candidate.Start <= current.End)
            {
                var end = candidate.End >= current.End ? candidate.End : current.End;
                current = new TextRange(current.Start, end);
                continue;
            }

            merged.Add(current);
            current = candidate;
        }

        merged.Add(current);
        return merged.ToArray();
    }

    private static int CompareRanges(TextRange left, TextRange right)
    {
        var startCompare = left.Start.CompareTo(right.Start);
        if (startCompare != 0)
        {
            return startCompare;
        }

        return left.End.CompareTo(right.End);
    }

    private void DeleteSelectionRanges(IReadOnlyList<TextRange> ranges)
    {
        for (var i = ranges.Count - 1; i >= 0; i--)
        {
            var range = ranges[i];
            if (range.IsEmpty)
            {
                continue;
            }

            _session.SetSelection(range);
            _session.Backspace();
        }
    }

    private bool TryCloneSelectedFloatingObjects(out List<FloatingObject> floatingObjects)
    {
        floatingObjects = new List<FloatingObject>();
        var selectedIds = GetSelectedFloatingIds();
        if (selectedIds.Count == 0)
        {
            return false;
        }

        var lookup = new Dictionary<Guid, FloatingObject>();
        var paragraphCount = _session.Document.ParagraphCount;
        for (var i = 0; i < paragraphCount; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            foreach (var candidate in paragraph.FloatingObjects)
            {
                if (!ContainsFloatingId(selectedIds, candidate.Id))
                {
                    continue;
                }

                lookup[candidate.Id] = DocumentClone.CloneFloatingObject(candidate);
            }
        }

        for (var i = 0; i < selectedIds.Count; i++)
        {
            if (lookup.TryGetValue(selectedIds[i], out var clone))
            {
                floatingObjects.Add(clone);
            }
        }

        return floatingObjects.Count > 0;
    }

    private bool DeleteSelectedFloatingObjects()
    {
        var selectedIds = GetSelectedFloatingIds();
        if (selectedIds.Count == 0)
        {
            return false;
        }

        var removed = false;
        var paragraphCount = _session.Document.ParagraphCount;
        for (var i = 0; i < paragraphCount; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
            for (var j = paragraph.FloatingObjects.Count - 1; j >= 0; j--)
            {
                if (!ContainsFloatingId(selectedIds, paragraph.FloatingObjects[j].Id))
                {
                    continue;
                }

                paragraph.FloatingObjects.RemoveAt(j);
                removed = true;
            }
        }

        if (removed)
        {
            _session.SetSelection(new TextRange(_session.Caret, _session.Caret));
        }

        return removed;
    }

    private string BuildPlainText(ClipboardContent content)
    {
        switch (content.Kind)
        {
            case ClipboardContentKind.FloatingObject:
                var floatingCount = content.FloatingObjects?.Count ?? (content.FloatingObject is null ? 0 : 1);
                return floatingCount <= 0
                    ? string.Empty
                    : new string(DocumentConstants.ObjectReplacementChar, floatingCount);
            case ClipboardContentKind.Blocks:
                if (content.Fragment is null)
                {
                    return string.Empty;
                }

                return BuildPlainText(content.Fragment.Blocks);
            default:
                return string.Empty;
        }
    }

    private static string BuildPlainText(IReadOnlyList<Block> blocks)
    {
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            switch (blocks[i])
            {
                case ParagraphBlock paragraph:
                    builder.Append(DocumentEditHelpers.GetParagraphText(paragraph));
                    break;
                case TableBlock table:
                    AppendTableText(builder, table);
                    break;
            }
        }

        return builder.ToString();
    }

    private static void AppendTableText(StringBuilder builder, TableBlock table)
    {
        var firstRow = true;
        foreach (var row in table.Rows)
        {
            if (!firstRow)
            {
                builder.Append('\n');
            }

            firstRow = false;
            var firstCell = true;
            foreach (var cell in row.Cells)
            {
                if (!firstCell)
                {
                    builder.Append('\t');
                }

                firstCell = false;
                var cellText = BuildCellText(cell);
                builder.Append(cellText);
            }
        }
    }

    private static string BuildCellText(TableCell cell)
    {
        if (cell.Paragraphs.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < cell.Paragraphs.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(DocumentEditHelpers.GetParagraphText(cell.Paragraphs[i]));
        }

        return builder.ToString();
    }
}

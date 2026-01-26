using System.Globalization;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

internal sealed class EditorTextClipboard
{
    private readonly IEditorMutableSession _session;
    private readonly IClipboardService _clipboard;
    private ClipboardFragment? _lastFragment;

    public EditorTextClipboard(IEditorMutableSession session, IClipboardService clipboard)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
    }

    public bool CopySelection()
    {
        var selection = _session.Selection;
        if (selection.IsEmpty)
        {
            return false;
        }

        var text = BuildSelectionText(selection);
        if (text.Length == 0)
        {
            return false;
        }

        _lastFragment = BuildSelectionFragment(selection);
        _clipboard.SetText(text);
        return true;
    }

    public bool CutSelection()
    {
        if (!CopySelection())
        {
            return false;
        }

        if (!_session.Selection.IsEmpty)
        {
            _session.Backspace();
        }

        return true;
    }

    public bool PasteText()
    {
        if (!_clipboard.TryGetText(out var text))
        {
            return false;
        }

        return PasteText(text.AsSpan());
    }

    public bool PasteKeepSource()
    {
        if (_lastFragment is null || _lastFragment.Paragraphs.Count == 0)
        {
            return PasteText();
        }

        return PasteFragment(_lastFragment);
    }

    public bool PasteMatchDestination() => PasteText();

    public bool PasteTextOnly() => PasteText();

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

    private bool PasteFragment(ClipboardFragment fragment)
    {
        if (fragment.Paragraphs.Count == 0)
        {
            return false;
        }

        var inserted = false;
        for (var i = 0; i < fragment.Paragraphs.Count; i++)
        {
            if (i > 0)
            {
                _session.InsertParagraphBreak();
                inserted = true;
            }

            var paragraph = fragment.Paragraphs[i];
            if (paragraph.Inlines.Count == 0)
            {
                continue;
            }

            _session.InsertInlines(paragraph.Inlines);
            inserted = true;
        }

        return inserted;
    }

    private ClipboardFragment BuildSelectionFragment(TextRange range)
    {
        var selection = range.Normalize();
        var fragment = new ClipboardFragment();
        var paragraphs = GetParagraphs();
        if (paragraphs.Count == 0)
        {
            return fragment;
        }

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

            var paragraphFragment = BuildParagraphFragment(paragraph, startOffset, endOffset);
            fragment.Paragraphs.Add(paragraphFragment);
        }

        return fragment;
    }

    private static ClipboardParagraph BuildParagraphFragment(ParagraphBlock paragraph, int startOffset, int endOffset)
    {
        var fragment = new ClipboardParagraph();
        if (startOffset >= endOffset)
        {
            return fragment;
        }

        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            var slice = SliceString(text, startOffset, endOffset - startOffset);
            if (slice.Length > 0)
            {
                fragment.Inlines.Add(new RunInline(slice));
            }

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
                fragment.Inlines.Add(CloneInlineOrPlaceholder(inline));
            }
        }

        return fragment;
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

    private static Inline CloneInlineOrPlaceholder(Inline inline)
    {
        try
        {
            return DocumentClone.CloneInline(inline);
        }
        catch (NotSupportedException)
        {
            return new RunInline(DocumentConstants.ObjectReplacementChar.ToString())
            {
                Hyperlink = CloneHyperlink(inline.Hyperlink)
            };
        }
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

    private string BuildSelectionText(TextRange range)
    {
        var selection = range.Normalize();
        var paragraphs = GetParagraphs();
        if (paragraphs.Count == 0)
        {
            return string.Empty;
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphs.Count - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphs.Count - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var capacity = EstimateSelectionLength(paragraphs, selection, startIndex, endIndex);
        var builder = new StringBuilder(capacity);

        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = paragraphs[i];
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? selection.Start.Offset : 0;
            var endOffset = i == endIndex ? selection.End.Offset : paragraphLength;

            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);
            if (endOffset > startOffset)
            {
                AppendParagraphSlice(builder, paragraph, startOffset, endOffset);
            }

            if (i < endIndex)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private int EstimateSelectionLength(IReadOnlyList<ParagraphBlock> paragraphs, TextRange selection, int startIndex, int endIndex)
    {
        var total = 0;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = paragraphs[i];
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? selection.Start.Offset : 0;
            var endOffset = i == endIndex ? selection.End.Offset : paragraphLength;

            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);
            if (endOffset > startOffset)
            {
                total += endOffset - startOffset;
            }

            if (i < endIndex)
            {
                total += 1;
            }
        }

        return total;
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

    private static void AppendParagraphSlice(StringBuilder builder, ParagraphBlock paragraph, int startOffset, int endOffset)
    {
        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            AppendStringSlice(builder, text, startOffset, endOffset - startOffset);
            return;
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

            var sliceStart = Math.Max(startOffset, inlineStart) - inlineStart;
            var sliceEnd = Math.Min(endOffset, inlineEnd) - inlineStart;
            AppendInlineSlice(builder, inline, sliceStart, sliceEnd - sliceStart);
        }
    }

    private static void AppendInlineSlice(StringBuilder builder, Inline inline, int start, int length)
    {
        if (length <= 0)
        {
            return;
        }

        switch (inline)
        {
            case RunInline run:
                builder.Append(run.Text.GetSlice(start, length));
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
            case FootnoteReferenceInline footnote:
                AppendStringSlice(builder, footnote.Id.ToString(CultureInfo.InvariantCulture), start, length);
                break;
            case EndnoteReferenceInline endnote:
                AppendStringSlice(builder, endnote.Id.ToString(CultureInfo.InvariantCulture), start, length);
                break;
            case CommentReferenceInline comment:
                AppendStringSlice(builder, comment.Id.ToString(CultureInfo.InvariantCulture), start, length);
                break;
            default:
                builder.Append(DocumentConstants.ObjectReplacementChar);
                break;
        }
    }

    private static void AppendStringSlice(StringBuilder builder, string text, int start, int length)
    {
        if (string.IsNullOrEmpty(text) || length <= 0)
        {
            return;
        }

        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        if (length == 0)
        {
            return;
        }

        builder.Append(text.AsSpan(start, length));
    }

    private sealed class ClipboardFragment
    {
        public List<ClipboardParagraph> Paragraphs { get; } = new();
    }

    private sealed class ClipboardParagraph
    {
        public List<Inline> Inlines { get; } = new();
    }
}

using System.Globalization;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

internal sealed class EditorTextClipboard
{
    private readonly IEditorMutableSession _session;
    private readonly IClipboardService _clipboard;

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

    private string BuildSelectionText(TextRange range)
    {
        var selection = range.Normalize();
        if (_session.Document.ParagraphCount == 0)
        {
            return string.Empty;
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var capacity = EstimateSelectionLength(selection, startIndex, endIndex);
        var builder = new StringBuilder(capacity);

        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
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

    private int EstimateSelectionLength(TextRange selection, int startIndex, int endIndex)
    {
        var total = 0;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
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
}

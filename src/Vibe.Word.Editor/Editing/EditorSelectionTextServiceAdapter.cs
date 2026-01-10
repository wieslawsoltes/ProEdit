using System.Globalization;
using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorSelectionTextServiceAdapter : ISelectionTextService
{
    private readonly IEditorMutableSession _session;

    public EditorSelectionTextServiceAdapter(IEditorMutableSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool TryGetSelectionText(out string text, int maxLength = 0)
    {
        var selection = _session.Selection.Normalize();
        if (selection.IsEmpty)
        {
            text = string.Empty;
            return false;
        }

        if (_session.Document.ParagraphCount == 0)
        {
            text = string.Empty;
            return false;
        }

        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var limit = maxLength > 0 ? maxLength : int.MaxValue;
        var capacity = Math.Min(EstimateSelectionLength(selection, startIndex, endIndex), limit);
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
                if (!AppendParagraphSlice(builder, paragraph, startOffset, endOffset, limit))
                {
                    break;
                }
            }

            if (i < endIndex)
            {
                if (!AppendChar(builder, '\n', limit))
                {
                    break;
                }
            }
        }

        text = builder.ToString();
        return text.Length > 0;
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

    private static bool AppendParagraphSlice(StringBuilder builder, ParagraphBlock paragraph, int startOffset, int endOffset, int maxLength)
    {
        if (paragraph.Inlines.Count == 0)
        {
            var text = paragraph.Text ?? string.Empty;
            return AppendStringSlice(builder, text, startOffset, endOffset - startOffset, maxLength);
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
            if (!AppendInlineSlice(builder, inline, sliceStart, sliceEnd - sliceStart, maxLength))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AppendInlineSlice(StringBuilder builder, Inline inline, int start, int length, int maxLength)
    {
        if (length <= 0)
        {
            return true;
        }

        switch (inline)
        {
            case RunInline run:
                return AppendStringSlice(builder, run.Text.GetSlice(start, length), 0, length, maxLength);
            case ImageInline:
            case ShapeInline:
            case ChartInline:
            case EquationInline:
            case PageNumberInline:
            case TotalPagesInline:
                return AppendChar(builder, DocumentConstants.ObjectReplacementChar, maxLength);
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
                return true;
            case FootnoteReferenceInline footnote:
                return AppendStringSlice(builder, footnote.Id.ToString(CultureInfo.InvariantCulture), start, length, maxLength);
            case EndnoteReferenceInline endnote:
                return AppendStringSlice(builder, endnote.Id.ToString(CultureInfo.InvariantCulture), start, length, maxLength);
            case CommentReferenceInline comment:
                return AppendStringSlice(builder, comment.Id.ToString(CultureInfo.InvariantCulture), start, length, maxLength);
            default:
                return AppendChar(builder, DocumentConstants.ObjectReplacementChar, maxLength);
        }
    }

    private static bool AppendStringSlice(StringBuilder builder, string text, int start, int length, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || length <= 0)
        {
            return true;
        }

        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        if (length == 0)
        {
            return true;
        }

        return AppendSpan(builder, text.AsSpan(start, length), maxLength);
    }

    private static bool AppendSpan(StringBuilder builder, ReadOnlySpan<char> span, int maxLength)
    {
        if (maxLength == int.MaxValue)
        {
            builder.Append(span);
            return true;
        }

        var remaining = maxLength - builder.Length;
        if (remaining <= 0)
        {
            return false;
        }

        if (span.Length > remaining)
        {
            builder.Append(span.Slice(0, remaining));
            return false;
        }

        builder.Append(span);
        return builder.Length < maxLength;
    }

    private static bool AppendChar(StringBuilder builder, char value, int maxLength)
    {
        if (maxLength != int.MaxValue && builder.Length >= maxLength)
        {
            return false;
        }

        builder.Append(value);
        return maxLength == int.MaxValue || builder.Length < maxLength;
    }
}

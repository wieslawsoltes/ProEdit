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
        var limit = maxLength > 0 ? maxLength : int.MaxValue;
        var selectionRanges = GetNormalizedSelectionRanges();
        if (selectionRanges.Length == 0)
        {
            var floatingCount = _session.SelectedFloatingObjectIds.Count;
            if (floatingCount > 0)
            {
                var count = limit == int.MaxValue ? floatingCount : Math.Min(floatingCount, limit);
                text = count > 0 ? new string(DocumentConstants.ObjectReplacementChar, count) : string.Empty;
                return true;
            }

            text = string.Empty;
            return false;
        }

        var paragraphs = GetParagraphs();
        if (paragraphs.Count == 0)
        {
            text = string.Empty;
            return false;
        }

        var estimatedLength = EstimateSelectionLength(paragraphs, selectionRanges);
        var capacity = Math.Min(estimatedLength, limit);
        var builder = new StringBuilder(capacity);

        for (var rangeIndex = 0; rangeIndex < selectionRanges.Length; rangeIndex++)
        {
            if (rangeIndex > 0)
            {
                if (!AppendChar(builder, '\n', limit))
                {
                    break;
                }
            }

            if (!AppendSelectionRange(builder, paragraphs, selectionRanges[rangeIndex], limit))
            {
                break;
            }
        }

        text = builder.ToString();
        return text.Length > 0;
    }

    private static int EstimateSelectionLength(IReadOnlyList<ParagraphBlock> paragraphs, IReadOnlyList<TextRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return 0;
        }

        var total = 0;
        for (var i = 0; i < ranges.Count; i++)
        {
            total += EstimateSelectionLength(paragraphs, ranges[i]);
            if (i + 1 < ranges.Count)
            {
                total += 1;
            }
        }

        return total;
    }

    private static int EstimateSelectionLength(IReadOnlyList<ParagraphBlock> paragraphs, TextRange selection)
    {
        if (paragraphs.Count == 0)
        {
            return 0;
        }

        var normalized = selection.Normalize();
        var startIndex = Math.Clamp(normalized.Start.ParagraphIndex, 0, paragraphs.Count - 1);
        var endIndex = Math.Clamp(normalized.End.ParagraphIndex, 0, paragraphs.Count - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var total = 0;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = paragraphs[i];
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? normalized.Start.Offset : 0;
            var endOffset = i == endIndex ? normalized.End.Offset : paragraphLength;

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

    private static bool AppendSelectionRange(StringBuilder builder, IReadOnlyList<ParagraphBlock> paragraphs, TextRange selection, int limit)
    {
        if (paragraphs.Count == 0)
        {
            return false;
        }

        var normalized = selection.Normalize();
        var startIndex = Math.Clamp(normalized.Start.ParagraphIndex, 0, paragraphs.Count - 1);
        var endIndex = Math.Clamp(normalized.End.ParagraphIndex, 0, paragraphs.Count - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = paragraphs[i];
            var paragraphLength = DocumentEditHelpers.GetParagraphLength(paragraph);
            var startOffset = i == startIndex ? normalized.Start.Offset : 0;
            var endOffset = i == endIndex ? normalized.End.Offset : paragraphLength;

            startOffset = Math.Clamp(startOffset, 0, paragraphLength);
            endOffset = Math.Clamp(endOffset, 0, paragraphLength);
            if (endOffset > startOffset)
            {
                if (!AppendParagraphSlice(builder, paragraph, startOffset, endOffset, limit))
                {
                    return false;
                }
            }

            if (i < endIndex)
            {
                if (!AppendChar(builder, '\n', limit))
                {
                    return false;
                }
            }
        }

        return true;
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

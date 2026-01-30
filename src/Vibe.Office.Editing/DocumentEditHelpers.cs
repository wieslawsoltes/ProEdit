using System.Globalization;
using System.Text;
using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public static class DocumentEditHelpers
{
    public static List<ParagraphBlock> BuildParagraphList(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = new List<ParagraphBlock>();
        void AppendBlocks(IReadOnlyList<Block> blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case ParagraphBlock paragraph:
                        paragraphs.Add(paragraph);
                        break;
                    case TableBlock table:
                        foreach (var row in table.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                AppendBlocks(cell.Blocks);
                            }
                        }

                        break;
                }
            }
        }

        AppendBlocks(document.Blocks);

        return paragraphs;
    }

    public static int GetParagraphLength(ParagraphBlock paragraph)
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

    public static int GetInlineLength(Inline inline)
    {
        return inline switch
        {
            RunInline run => run.Text.Length,
            ImageInline => 1,
            ShapeInline => 1,
            ChartInline => 1,
            EquationInline => 1,
            PageNumberInline => 1,
            TotalPagesInline => 1,
            FootnoteReferenceInline footnote => footnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length,
            EndnoteReferenceInline endnote => endnote.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length,
            CommentReferenceInline comment => comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length,
            MetadataStartInline => 0,
            MetadataEndInline => 0,
            FieldStartInline => 0,
            FieldSeparatorInline => 0,
            FieldEndInline => 0,
            BookmarkStartInline => 0,
            BookmarkEndInline => 0,
            CommentRangeStartInline => 0,
            CommentRangeEndInline => 0,
            ContentControlStartInline => 0,
            ContentControlEndInline => 0,
            RevisionStartInline => 0,
            RevisionEndInline => 0,
            RevisionRangeStartInline => 0,
            RevisionRangeEndInline => 0,
            _ => 1
        };
    }

    public static string GetParagraphText(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return paragraph.Text ?? string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
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
                    builder.Append(footnote.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                case EndnoteReferenceInline endnote:
                    builder.Append(endnote.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                case CommentReferenceInline comment:
                    builder.Append(comment.Id.ToString(CultureInfo.InvariantCulture));
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

    public static bool WrapRangeWithRevisionMarkers(
        ParagraphBlock paragraph,
        int startOffset,
        int endOffset,
        RevisionInfo revision)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(revision);

        EnsureParagraphInlines(paragraph);
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var length = GetParagraphLength(paragraph);
        var start = Math.Clamp(startOffset, 0, length);
        var end = Math.Clamp(endOffset, 0, length);
        if (end <= start)
        {
            return false;
        }

        var newInlines = new List<Inline>(paragraph.Inlines.Count + 2);
        var position = 0;
        var started = false;
        var ended = false;

        foreach (var inline in paragraph.Inlines)
        {
            var inlineLength = GetInlineLength(inline);
            if (inlineLength == 0)
            {
                newInlines.Add(inline);
                continue;
            }

            var inlineStart = position;
            var inlineEnd = position + inlineLength;
            position = inlineEnd;

            if (inlineEnd <= start || inlineStart >= end)
            {
                if (started && !ended && inlineStart >= end)
                {
                    newInlines.Add(new RevisionEndInline(revision.Kind, revision.Id));
                    ended = true;
                }

                newInlines.Add(inline);
                continue;
            }

            if (inline is RunInline run)
            {
                var runLength = run.Text.Length;
                var selectionStart = Math.Max(start, inlineStart) - inlineStart;
                var selectionEnd = Math.Min(end, inlineEnd) - inlineStart;

                if (selectionStart > 0)
                {
                    newInlines.Add(CloneRunSlice(run, 0, selectionStart));
                }

                if (!started)
                {
                    newInlines.Add(new RevisionStartInline(revision));
                    started = true;
                }

                if (selectionEnd > selectionStart)
                {
                    newInlines.Add(CloneRunSlice(run, selectionStart, selectionEnd - selectionStart));
                }

                if (selectionEnd < runLength)
                {
                    if (!ended)
                    {
                        newInlines.Add(new RevisionEndInline(revision.Kind, revision.Id));
                        ended = true;
                    }

                    newInlines.Add(CloneRunSlice(run, selectionEnd, runLength - selectionEnd));
                }
            }
            else
            {
                if (!started)
                {
                    newInlines.Add(new RevisionStartInline(revision));
                    started = true;
                }

                newInlines.Add(inline);
                if (!ended && inlineEnd >= end)
                {
                    newInlines.Add(new RevisionEndInline(revision.Kind, revision.Id));
                    ended = true;
                }
            }
        }

        if (started && !ended)
        {
            newInlines.Add(new RevisionEndInline(revision.Kind, revision.Id));
        }

        if (!started)
        {
            return false;
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(newInlines);
        return true;
    }

    private static RunInline CloneRunSlice(RunInline run, int start, int length)
    {
        var buffer = run.Text.SliceBuffer(start, length);
        var clone = new RunInline(buffer, run.Style)
        {
            StyleId = run.StyleId,
            Hyperlink = run.Hyperlink
        };
        return clone;
    }

    public static void EnsureParagraphInlines(ParagraphBlock paragraph)
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

    public static EquationInline? FindEquationInline(ParagraphBlock paragraph, int offset)
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

    public static bool TryFindContentControlAtOffset(
        ParagraphBlock paragraph,
        int offset,
        out ContentControlProperties properties)
    {
        properties = null!;
        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var stack = new List<ContentControlRangeState>();
        var bestSpan = int.MaxValue;
        var found = false;
        var position = 0;

        foreach (var inline in paragraph.Inlines)
        {
            if (inline is ContentControlStartInline start)
            {
                stack.Add(new ContentControlRangeState(start.Properties, position));
            }

            var inlineLength = GetInlineLength(inline);
            if (inlineLength > 0)
            {
                if (offset >= position && offset < position + inlineLength && stack.Count > 0)
                {
                    properties = stack[^1].Properties;
                    return true;
                }

                position += inlineLength;
            }

            if (inline is ContentControlEndInline)
            {
                if (stack.Count == 0)
                {
                    continue;
                }

                var state = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (offset >= state.StartOffset && offset <= position)
                {
                    var span = position - state.StartOffset;
                    if (!found || span <= bestSpan)
                    {
                        properties = state.Properties;
                        bestSpan = span;
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    public static bool TryFindContentControlAtLayoutOffset(
        Document document,
        ParagraphBlock paragraph,
        int layoutOffset,
        out ContentControlProperties properties,
        out int documentOffset)
    {
        properties = null!;
        documentOffset = 0;

        if (paragraph.Inlines.Count == 0)
        {
            return false;
        }

        var stack = new List<ContentControlLayoutState>();
        var layoutPosition = 0;
        var docPosition = 0;

        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case ContentControlStartInline start:
                {
                    var placeholderText = ContentControlValueResolver.ResolvePlaceholderText(start.Properties);
                    var boundValue = ContentControlValueResolver.ResolveContentControlValue(start.Properties, document);
                    stack.Add(new ContentControlLayoutState(start.Properties, docPosition, placeholderText, boundValue));
                    break;
                }
                case ContentControlEndInline:
                {
                    if (stack.Count == 0)
                    {
                        break;
                    }

                    var state = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    if (!state.HasContent)
                    {
                        if (!string.IsNullOrWhiteSpace(state.BoundValue))
                        {
                            if (layoutOffset >= layoutPosition && layoutOffset < layoutPosition + state.BoundValue.Length)
                            {
                                properties = state.Properties;
                                documentOffset = state.StartDocumentOffset;
                                return true;
                            }

                            layoutPosition += state.BoundValue.Length;
                        }
                        else if (state.ShouldShowPlaceholder && !string.IsNullOrWhiteSpace(state.PlaceholderText))
                        {
                            if (layoutOffset >= layoutPosition && layoutOffset < layoutPosition + state.PlaceholderText.Length)
                            {
                                properties = state.Properties;
                                documentOffset = state.StartDocumentOffset;
                                return true;
                            }

                            layoutPosition += state.PlaceholderText.Length;
                        }
                    }

                    break;
                }
                default:
                {
                    var inlineLength = GetInlineLength(inline);
                    if (inlineLength <= 0)
                    {
                        break;
                    }

                    if (layoutOffset >= layoutPosition && layoutOffset < layoutPosition + inlineLength && stack.Count > 0)
                    {
                        properties = stack[^1].Properties;
                        documentOffset = docPosition + (layoutOffset - layoutPosition);
                        return true;
                    }

                    if (stack.Count > 0)
                    {
                        for (var i = 0; i < stack.Count; i++)
                        {
                            stack[i].HasContent = true;
                        }
                    }

                    layoutPosition += inlineLength;
                    docPosition += inlineLength;
                    break;
                }
            }
        }

        return false;
    }

    public static bool IsContentControlContentLocked(ContentControlProperties properties)
    {
        if (string.IsNullOrWhiteSpace(properties.Lock))
        {
            return false;
        }

        return properties.Lock.Equals("contentLocked", StringComparison.OrdinalIgnoreCase)
            || properties.Lock.Equals("sdtContentLocked", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsContentControlSdtLocked(ContentControlProperties properties)
    {
        if (string.IsNullOrWhiteSpace(properties.Lock))
        {
            return false;
        }

        return properties.Lock.Equals("sdtLocked", StringComparison.OrdinalIgnoreCase)
            || properties.Lock.Equals("sdtContentLocked", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ContentControlRangeState
    {
        public ContentControlProperties Properties { get; }
        public int StartOffset { get; }

        public ContentControlRangeState(ContentControlProperties properties, int startOffset)
        {
            Properties = properties;
            StartOffset = startOffset;
        }
    }

    private sealed class ContentControlLayoutState
    {
        public ContentControlProperties Properties { get; }
        public int StartDocumentOffset { get; }
        public string PlaceholderText { get; }
        public string? BoundValue { get; }
        public bool ShouldShowPlaceholder { get; }
        public bool HasContent { get; set; }

        public ContentControlLayoutState(
            ContentControlProperties properties,
            int startDocumentOffset,
            string placeholderText,
            string? boundValue)
        {
            Properties = properties;
            StartDocumentOffset = startDocumentOffset;
            PlaceholderText = placeholderText;
            BoundValue = boundValue;
            ShouldShowPlaceholder = properties.ShowingPlaceholder != false;
        }
    }
}

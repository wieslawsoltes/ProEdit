using System.Text;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

internal static class ReviewingHelpers
{
    public static Dictionary<int, TextPosition> BuildCommentAnchors(Document document)
    {
        var anchors = new Dictionary<int, TextPosition>();
        foreach (var (paragraph, paragraphIndex) in EnumerateParagraphs(document))
        {
            if (paragraph.Inlines.Count == 0)
            {
                continue;
            }

            var offset = 0;
            foreach (var inline in paragraph.Inlines)
            {
                switch (inline)
                {
                    case CommentRangeStartInline start:
                        anchors.TryAdd(start.Id, new TextPosition(paragraphIndex, offset));
                        break;
                    case CommentReferenceInline reference:
                        anchors.TryAdd(reference.Id, new TextPosition(paragraphIndex, offset));
                        break;
                }

                offset += DocumentEditHelpers.GetInlineLength(inline);
            }
        }

        return anchors;
    }

    public static string BuildCommentText(CommentDefinition comment)
    {
        if (comment.Blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var block in comment.Blocks)
        {
            if (block is ParagraphBlock paragraph)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(DocumentEditHelpers.GetParagraphText(paragraph));
            }
        }

        return builder.ToString();
    }

    public static string BuildCommentDisplayText(CommentDefinition comment)
    {
        var body = BuildCommentText(comment);
        var builder = new StringBuilder();
        var hasHeader = false;

        if (!string.IsNullOrWhiteSpace(comment.Author))
        {
            builder.Append(comment.Author);
            hasHeader = true;
        }

        if (comment.Date.HasValue)
        {
            if (hasHeader)
            {
                builder.Append(' ');
            }

            builder.Append(comment.Date.Value.ToLocalTime().ToString("g"));
            hasHeader = true;
        }

        if (hasHeader && !string.IsNullOrWhiteSpace(body))
        {
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.Append(body);
        }

        return builder.ToString();
    }

    public static void UpdateCommentText(CommentDefinition comment, string text)
    {
        comment.Blocks.Clear();
        if (string.IsNullOrEmpty(text))
        {
            comment.Blocks.Add(new ParagraphBlock());
            return;
        }

        var span = text.AsSpan();
        var start = 0;

        for (var i = 0; i <= span.Length; i++)
        {
            if (i < span.Length && span[i] != '\n')
            {
                continue;
            }

            var line = span.Slice(start, i - start);
            if (line.Length > 0 && line[^1] == '\r')
            {
                line = line[..^1];
            }

            var paragraphText = line.Length == 0 ? string.Empty : new string(line);
            comment.Blocks.Add(new ParagraphBlock(paragraphText));
            start = i + 1;
        }

        if (comment.Blocks.Count == 0)
        {
            comment.Blocks.Add(new ParagraphBlock());
        }
    }

    public static List<ReviewRevisionAnchor> BuildRevisionAnchors(Document document)
    {
        var anchors = new List<ReviewRevisionAnchor>();
        var paragraphIndex = 0;

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case RevisionStartBlock start:
                    anchors.Add(new ReviewRevisionAnchor(start.Revision, new TextPosition(paragraphIndex, 0), true));
                    break;
                case RevisionEndBlock:
                    break;
                case ParagraphBlock paragraph:
                    CollectInlineRevisions(paragraph, paragraphIndex, anchors);
                    paragraphIndex++;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                CollectInlineRevisions(paragraph, paragraphIndex, anchors);
                                paragraphIndex++;
                            }
                        }
                    }

                    break;
            }
        }

        return anchors;
    }

    private static void CollectInlineRevisions(
        ParagraphBlock paragraph,
        int paragraphIndex,
        List<ReviewRevisionAnchor> anchors)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return;
        }

        var offset = 0;
        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case RevisionStartInline start:
                    anchors.Add(new ReviewRevisionAnchor(start.Revision, new TextPosition(paragraphIndex, offset), false));
                    break;
                case RevisionRangeStartInline rangeStart:
                    anchors.Add(new ReviewRevisionAnchor(rangeStart.Revision, new TextPosition(paragraphIndex, offset), false));
                    break;
            }

            offset += DocumentEditHelpers.GetInlineLength(inline);
        }
    }

    private static IEnumerable<(ParagraphBlock Paragraph, int ParagraphIndex)> EnumerateParagraphs(Document document)
    {
        var paragraphIndex = 0;
        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    yield return (paragraph, paragraphIndex++);
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                yield return (paragraph, paragraphIndex++);
                            }
                        }
                    }

                    break;
            }
        }
    }
}

internal readonly record struct ReviewRevisionAnchor(
    RevisionInfo Revision,
    TextPosition Anchor,
    bool IsBlock);

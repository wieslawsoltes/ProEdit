using System.Text;
using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

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
        var hasHeader = AppendCommentHeader(builder, comment, isReply: false, showResolved: comment.IsResolved);
        AppendCommentBody(builder, body, hasHeader);
        return builder.ToString();
    }

    public static string BuildCommentDisplayText(Document document, CommentDefinition comment)
    {
        var root = CommentThreading.ResolveRootComment(comment, document.Comments);
        var builder = new StringBuilder();

        var rootBody = BuildCommentText(root);
        var hasHeader = AppendCommentHeader(builder, root, isReply: false, showResolved: root.IsResolved);
        AppendCommentBody(builder, rootBody, hasHeader);

        var replies = CollectThreadReplies(document, root.Id);
        foreach (var reply in replies)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            var replyBody = BuildCommentText(reply);
            var replyHeader = AppendCommentHeader(builder, reply, isReply: true, showResolved: false);
            AppendCommentBody(builder, replyBody, replyHeader);
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

    private static List<CommentDefinition> CollectThreadReplies(Document document, int threadId)
    {
        var replies = new List<CommentDefinition>();
        foreach (var comment in document.Comments.Values)
        {
            if (comment.Id == threadId)
            {
                continue;
            }

            if (CommentThreading.ResolveThreadId(comment, document.Comments) == threadId)
            {
                replies.Add(comment);
            }
        }

        replies.Sort(CompareCommentThreadItems);
        return replies;
    }

    private static int CompareCommentThreadItems(CommentDefinition left, CommentDefinition right)
    {
        var leftDate = left.Date ?? DateTime.MinValue;
        var rightDate = right.Date ?? DateTime.MinValue;
        var dateCompare = leftDate.CompareTo(rightDate);
        if (dateCompare != 0)
        {
            return dateCompare;
        }

        return left.Id.CompareTo(right.Id);
    }

    private static bool AppendCommentHeader(StringBuilder builder, CommentDefinition comment, bool isReply, bool showResolved)
    {
        var hasHeader = false;

        if (isReply)
        {
            builder.Append("Reply");
            hasHeader = true;
        }

        if (!string.IsNullOrWhiteSpace(comment.Author))
        {
            if (hasHeader)
            {
                builder.Append(" - ");
            }

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

        if (showResolved)
        {
            if (hasHeader)
            {
                builder.Append(' ');
            }

            builder.Append("(Resolved)");
            hasHeader = true;
        }

        return hasHeader;
    }

    private static void AppendCommentBody(StringBuilder builder, string body, bool hasHeader)
    {
        if (hasHeader && !string.IsNullOrWhiteSpace(body))
        {
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.Append(body);
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

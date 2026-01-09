using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public static class DocumentEditHelpers
{
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
            _ => 1
        };
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
}

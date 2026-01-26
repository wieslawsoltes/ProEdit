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
        foreach (var block in document.Blocks)
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
                            foreach (var cellParagraph in cell.Paragraphs)
                            {
                                paragraphs.Add(cellParagraph);
                            }
                        }
                    }

                    break;
            }
        }

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
                    break;
                default:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
            }
        }

        return builder.ToString();
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

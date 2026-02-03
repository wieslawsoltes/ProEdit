using System.Security.Cryptography;
using System.Text;
using Vibe.Office.Documents;

namespace Vibe.Office.Pdf.Documents;

public static class PdfDocumentHash
{
    public static string Compute(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = BuildPlainText(document.Blocks);
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(sha.ComputeHash(bytes));
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
                    builder.Append(GetParagraphText(paragraph));
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
                builder.Append(BuildCellText(cell));
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

            builder.Append(GetParagraphText(cell.Paragraphs[i]));
        }

        return builder.ToString();
    }

    private static string GetParagraphText(ParagraphBlock paragraph)
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
                case FootnoteReferenceInline footnote:
                    builder.Append(footnote.Id.ToString());
                    break;
                case EndnoteReferenceInline endnote:
                    builder.Append(endnote.Id.ToString());
                    break;
                case CommentReferenceInline comment:
                    builder.Append(comment.Id.ToString());
                    break;
                default:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
            }
        }

        return builder.ToString();
    }
}

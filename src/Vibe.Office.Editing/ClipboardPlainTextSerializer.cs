using System.Text;
using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public static class ClipboardPlainTextSerializer
{
    public static string ToPlainText(ClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return content.Kind switch
        {
            ClipboardContentKind.FloatingObject => BuildObjectText(content),
            ClipboardContentKind.Blocks when content.Fragment is not null => BuildPlainText(content.Fragment.Blocks),
            _ => string.Empty
        };
    }

    public static string ToPlainText(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return BuildPlainText(document.Blocks);
    }

    public static Document ToDocument(string text)
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Sections.Clear();
        document.Sections.Add(new DocumentSection(
            document.SectionProperties,
            document.Header,
            document.Footer,
            document.FirstHeader,
            document.FirstFooter,
            document.EvenHeader,
            document.EvenFooter));
        DocumentDefaults.ApplyDefaultPageSetup(document.SectionProperties);

        if (string.IsNullOrEmpty(text))
        {
            document.Blocks.Add(new ParagraphBlock());
            return document;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        foreach (var line in lines)
        {
            document.Blocks.Add(new ParagraphBlock(line));
        }

        return document;
    }

    private static string BuildObjectText(ClipboardContent content)
    {
        var count = content.FloatingObjects?.Count ?? (content.FloatingObject is null ? 0 : 1);
        return count <= 0 ? string.Empty : new string(DocumentConstants.ObjectReplacementChar, count);
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
                    builder.Append(DocumentEditHelpers.GetParagraphText(paragraph));
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
                var cellText = BuildCellText(cell);
                builder.Append(cellText);
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

            builder.Append(DocumentEditHelpers.GetParagraphText(cell.Paragraphs[i]));
        }

        return builder.ToString();
    }
}

using System.Text;
using Vibe.Office.Documents;

namespace Vibe.Office.Layout.Benchmarks;

internal static class DocumentBenchmarkFactory
{
    private static readonly string[] Words =
    [
        "lorem",
        "ipsum",
        "dolor",
        "sit",
        "amet",
        "consectetur",
        "adipiscing",
        "elit",
        "integer",
        "viverra",
        "tellus",
        "scelerisque",
        "pulvinar",
        "characteristically",
        "documentation",
        "synchronization",
        "performance",
        "benchmarking",
        "layout",
        "paragraph"
    ];

    public static Document CreateLargeDocument(
        int paragraphCount,
        int wordsPerParagraph = 80,
        int tableFrequency = 50)
    {
        var document = new Document();
        document.Blocks.Clear();
        for (var i = 0; i < paragraphCount; i++)
        {
            if (tableFrequency > 0 && i > 0 && i % tableFrequency == 0)
            {
                document.Blocks.Add(BuildTable(i));
            }

            var paragraphText = BuildParagraphText(i, wordsPerParagraph);
            document.Blocks.Add(new ParagraphBlock(paragraphText));
        }

        return document;
    }

    private static string BuildParagraphText(int seed, int wordsPerParagraph)
    {
        if (wordsPerParagraph <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(wordsPerParagraph * 6);
        for (var i = 0; i < wordsPerParagraph; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            var word = Words[(seed + i) % Words.Length];
            builder.Append(word);
        }

        return builder.ToString();
    }

    private static TableBlock BuildTable(int index)
    {
        var rows = new List<TableRow>();
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            var cells = new List<TableCell>();
            for (var columnIndex = 0; columnIndex < 3; columnIndex++)
            {
                var cellText = $"Cell {index}-{rowIndex}-{columnIndex}";
                var paragraph = new ParagraphBlock(cellText);
                cells.Add(new TableCell(new[] { paragraph }));
            }

            rows.Add(new TableRow(cells));
        }

        return new TableBlock(rows);
    }
}

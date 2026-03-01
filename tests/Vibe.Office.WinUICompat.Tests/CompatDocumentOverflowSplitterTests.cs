using Vibe.Office.WinUICompat.Bridges;
using Vibe.Office.WinUICompat.Documents;
using Xunit;

namespace Vibe.Office.WinUICompat.Tests;

public sealed class CompatDocumentOverflowSplitterTests
{
    [Fact]
    public void SplitByMaxLines_PreservesStructuredOverflowBlocks()
    {
        var source = new RichTextDocument();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("header line"));

        var table = new Table();
        var rowGroup = new TableRowGroup();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Blocks.Add(new Paragraph("table cell"));
        row.Cells.Add(cell);
        rowGroup.Rows.Add(row);
        table.RowGroups.Add(rowGroup);
        source.Blocks.Add(table);

        var splitter = new CompatDocumentOverflowSplitter();
        var split = splitter.SplitByMaxLines(source, viewportWidth: 800f, maxLines: 1);

        Assert.True(split.HasOverflow);
        Assert.Contains(split.OverflowDocument.Blocks, static block => block is Table);
    }
}

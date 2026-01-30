using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Xunit;

namespace Vibe.Office.Layout.Tests;

public sealed class TableAutoFitTests
{
    [Fact]
    public void AutoFitTable_UsesMaxWordWidthPerColumn()
    {
        var document = new Document();
        document.Blocks.Clear();

        var cellA = new TableCell(new[] { new ParagraphBlock("tiny") });
        var cellB = new TableCell(new[] { new ParagraphBlock("elephant") });
        var row = new TableRow(new[] { cellA, cellB });
        document.Blocks.Add(new TableBlock(new[] { row }));

        var settings = new LayoutSettings
        {
            UsePagination = false,
            ViewportWidth = 200f,
            ViewportHeight = 200f,
            MarginLeft = 0f,
            MarginRight = 0f,
            MarginTop = 0f,
            MarginBottom = 0f,
            TableCellPadding = 0f
        };

        var layout = new DocumentLayouter().Layout(document, settings, new TestTextMeasurer());
        var table = Assert.Single(layout.Tables);

        Assert.Equal(2, table.ColumnWidths.Count);
        Assert.Equal((float)"tiny".Length, table.ColumnWidths[0], 3);
        Assert.Equal((float)"elephant".Length, table.ColumnWidths[1], 3);
    }
}

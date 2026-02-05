using Avalonia.Controls;
using Vibe.Office.Documents;
using Vibe.Office.FlowDocument;
using Vibe.Office.FlowDocument.Documents;
using Xunit;

namespace Vibe.Office.FlowDocument.Tests;

public sealed class FlowDocumentConverterTests
{
    [Fact]
    public void ConvertsParagraphRunWithStyles()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        var bold = new Bold();
        bold.Inlines.Add(new Run("Hello"));
        paragraph.Inlines.Add(bold);
        document.Blocks.Add(paragraph);

        var converter = new FlowDocumentConverter();
        var result = converter.Convert(document);

        var paragraphBlock = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        var run = Assert.IsType<RunInline>(paragraphBlock.Inlines[0]);
        Assert.Equal("Hello", run.GetText());
        Assert.NotNull(run.Style);
        Assert.Equal(DocFontWeight.Bold, run.Style?.FontWeight);
    }

    [Fact]
    public void ConvertsLineBreaks()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("Hello"));
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run("World"));
        document.Blocks.Add(paragraph);

        var converter = new FlowDocumentConverter();
        var result = converter.Convert(document);

        var paragraphBlock = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        Assert.Contains(paragraphBlock.Inlines, inline => inline is RunInline run && run.GetText().Contains("\n", StringComparison.Ordinal));
    }

    [Fact]
    public void ConvertsHyperlinks()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        var link = new Hyperlink { NavigateUri = "https://example.com" };
        link.Inlines.Add(new Run("Link"));
        paragraph.Inlines.Add(link);
        document.Blocks.Add(paragraph);

        var converter = new FlowDocumentConverter();
        var result = converter.Convert(document);

        var paragraphBlock = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        var run = Assert.IsType<RunInline>(paragraphBlock.Inlines[0]);
        Assert.Equal("https://example.com", run.Hyperlink?.Uri);
    }

    [Fact]
    public void ConvertsListsWithStartIndex()
    {
        var document = new FlowDocument();
        var list = new List { MarkerStyle = FlowListMarkerStyle.Decimal, StartIndex = 3 };
        var item = new ListItem();
        item.Blocks.Add(new Paragraph("Item"));
        list.ListItems.Add(item);
        document.Blocks.Add(list);

        var converter = new FlowDocumentConverter();
        var result = converter.Convert(document);

        var paragraphBlock = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        Assert.NotNull(paragraphBlock.ListInfo);
        Assert.Equal(ListKind.Numbered, paragraphBlock.ListInfo?.Kind);
        Assert.Equal(3, paragraphBlock.ListInfo?.StartAt);
    }

    [Fact]
    public void ConvertsTables()
    {
        var document = new FlowDocument();
        var table = new Table();
        var group = new TableRowGroup();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Blocks.Add(new Paragraph("Cell"));
        row.Cells.Add(cell);
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        document.Blocks.Add(table);

        var converter = new FlowDocumentConverter();
        var result = converter.Convert(document);

        var tableBlock = Assert.IsType<TableBlock>(result.Blocks[0]);
        Assert.Single(tableBlock.Rows);
        Assert.Single(tableBlock.Rows[0].Cells);
        var paragraphBlock = Assert.IsType<ParagraphBlock>(tableBlock.Rows[0].Cells[0].Blocks[0]);
        var run = Assert.IsType<RunInline>(paragraphBlock.Inlines[0]);
        Assert.Equal("Cell", run.GetText());
    }

    [Fact]
    public void ConvertsRowSpans()
    {
        var document = new FlowDocument();
        var table = new Table();
        var group = new TableRowGroup();
        var row1 = new TableRow();
        var cell1 = new TableCell { RowSpan = 2 };
        cell1.Blocks.Add(new Paragraph("Span"));
        row1.Cells.Add(cell1);
        var cell2 = new TableCell();
        cell2.Blocks.Add(new Paragraph("Row1"));
        row1.Cells.Add(cell2);
        group.Rows.Add(row1);
        var row2 = new TableRow();
        var cell3 = new TableCell();
        cell3.Blocks.Add(new Paragraph("Row2"));
        row2.Cells.Add(cell3);
        group.Rows.Add(row2);
        table.RowGroups.Add(group);
        document.Blocks.Add(table);

        var converter = new FlowDocumentConverter();
        var result = converter.Convert(document);

        var tableBlock = Assert.IsType<TableBlock>(result.Blocks[0]);
        Assert.Equal(TableCellVerticalMerge.Restart, tableBlock.Rows[0].Cells[0].VerticalMerge);
        Assert.Equal(TableCellVerticalMerge.Continue, tableBlock.Rows[1].Cells[0].VerticalMerge);
    }

    [Fact]
    public void SupportsCustomUiPlaceholders()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new InlineUIContainer { Child = new object() });
        document.Blocks.Add(paragraph);
        document.Blocks.Add(new BlockUIContainer { Child = new object() });

        var converter = new FlowDocumentConverter(new FlowDocumentConverterOptions
        {
            InlineUiPlaceholderText = "[Inline]",
            BlockUiPlaceholderText = "[Block]"
        });
        var result = converter.Convert(document);

        var paragraphBlock = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        var inlineRun = Assert.IsType<RunInline>(paragraphBlock.Inlines[0]);
        Assert.Equal("[Inline]", inlineRun.GetText());

        var blockPlaceholder = Assert.IsType<ParagraphBlock>(result.Blocks[1]);
        var blockRun = Assert.IsType<RunInline>(blockPlaceholder.Inlines[0]);
        Assert.Equal("[Block]", blockRun.GetText());
    }

    [Fact]
    public void ConvertsFiguresToFloatingObjects()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        var figure = new Figure { Width = 200, Height = 120 };
        figure.Blocks.Add(new Paragraph("Figure text"));
        paragraph.Inlines.Add(figure);
        document.Blocks.Add(paragraph);

        var converter = new FlowDocumentConverter();
        var result = converter.Convert(document);

        var paragraphBlock = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        Assert.Single(paragraphBlock.FloatingObjects);
        var floating = paragraphBlock.FloatingObjects[0];
        var shape = Assert.IsType<ShapeInline>(floating.Content);
        Assert.NotNull(shape.TextBox);
        Assert.NotEmpty(shape.TextBox!.Blocks);
    }

    [Fact]
    public void ConvertsUiContainersToEmbeddedShapesWhenEnabled()
    {
        var document = new FlowDocument();
        var inlineParagraph = new Paragraph();
        inlineParagraph.Inlines.Add(new Run("Before "));
        inlineParagraph.Inlines.Add(new InlineUIContainer
        {
            Child = new Button
            {
                Content = "Inline",
                Width = 110,
                Height = 24
            }
        });
        inlineParagraph.Inlines.Add(new Run(" After"));
        document.Blocks.Add(inlineParagraph);

        document.Blocks.Add(new BlockUIContainer
        {
            Child = new Button
            {
                Content = "Block",
                Width = 240,
                Height = 96
            }
        });

        var converter = new FlowDocumentConverter(new FlowDocumentConverterOptions
        {
            EnableEmbeddedUiElements = true
        });
        var result = converter.Convert(document);

        var paragraph = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        Assert.Contains(paragraph.Inlines, inline => inline is ShapeInline);

        var blockParagraph = Assert.IsType<ParagraphBlock>(result.Blocks[1]);
        var blockShape = Assert.IsType<ShapeInline>(blockParagraph.Inlines[0]);
        Assert.Equal(240f, blockShape.Width);
        Assert.Equal(96f, blockShape.Height);
        Assert.True(FlowDocumentConverter.TryParseEmbeddedUiElementId(
            blockShape.Name,
            FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix,
            out var blockId));
        Assert.Equal(2, converter.EmbeddedUiElements.Count);
        Assert.Contains(converter.EmbeddedUiElements, element => element.Id == blockId && !element.IsInline);
    }
}

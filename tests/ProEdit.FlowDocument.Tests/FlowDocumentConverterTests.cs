using Avalonia.Controls;
using ProEdit.Documents;
using ProEdit.FlowDocument;
using ProEdit.FlowDocument.Documents;
using Xunit;

namespace ProEdit.FlowDocument.Tests;

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

    [Fact]
    public void ConvertsAnchoredUiContainerToEmbeddedFloatingShape_WhenEnabled()
    {
        var embeddedButton = new Button
        {
            Content = "Anchored",
            Width = 180,
            Height = 72
        };

        var document = new FlowDocument();
        var paragraph = new Paragraph("Anchor:");
        var figure = new Figure { Width = 180, Height = 72 };
        figure.Blocks.Add(new BlockUIContainer
        {
            Child = embeddedButton
        });
        paragraph.Inlines.Add(figure);
        document.Blocks.Add(paragraph);

        var converter = new FlowDocumentConverter(new FlowDocumentConverterOptions
        {
            EnableEmbeddedUiElements = true
        });
        var result = converter.Convert(document);

        var paragraphBlock = Assert.IsType<ParagraphBlock>(Assert.Single(result.Blocks));
        var floating = Assert.Single(paragraphBlock.FloatingObjects);
        var shape = Assert.IsType<ShapeInline>(floating.Content);
        Assert.True(FlowDocumentConverter.TryParseEmbeddedUiElementId(
            shape.Name,
            FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix,
            out var id));
        Assert.Equal(180f, shape.Width);
        Assert.Equal(72f, shape.Height);
        Assert.Contains(converter.EmbeddedUiElements, element => element.Id == id && !element.IsInline);
    }

    [Fact]
    public void ConvertsUiContainersToEmbeddedShapes_WithCustomPredicateAndSizeResolver()
    {
        var inlineChild = new EmbeddedTestChild();
        var blockChild = new EmbeddedTestChild();

        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new InlineUIContainer { Child = inlineChild });
        document.Blocks.Add(paragraph);
        document.Blocks.Add(new BlockUIContainer { Child = blockChild });

        var converter = new FlowDocumentConverter(new FlowDocumentConverterOptions
        {
            EnableEmbeddedUiElements = true,
            EmbeddedUiElementPredicate = static child => child is EmbeddedTestChild,
            EmbeddedUiSizeResolver = static (child, isInline) => child is EmbeddedTestChild
                ? (isInline ? 42d : 128d, isInline ? 19d : 64d)
                : null
        });

        var result = converter.Convert(document);

        var inlineParagraph = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        var inlineShape = Assert.IsType<ShapeInline>(inlineParagraph.Inlines[0]);
        Assert.Equal(42f, inlineShape.Width);
        Assert.Equal(19f, inlineShape.Height);

        var blockParagraph = Assert.IsType<ParagraphBlock>(result.Blocks[1]);
        var blockShape = Assert.IsType<ShapeInline>(blockParagraph.Inlines[0]);
        Assert.Equal(128f, blockShape.Width);
        Assert.Equal(64f, blockShape.Height);

        Assert.Equal(2, converter.EmbeddedUiElements.Count);
        Assert.Same(inlineChild, converter.EmbeddedUiElements[0].Child);
        Assert.Same(blockChild, converter.EmbeddedUiElements[1].Child);
    }

    [Fact]
    public void ConvertsTableCellVisualPropertiesToDocumentModel()
    {
        var document = new FlowDocument();
        var table = new Table();
        var rowGroup = new TableRowGroup();
        var row = new TableRow();
        var cell = new TableCell
        {
            Padding = new FlowThickness(6, 4, 6, 4),
            BorderThickness = new FlowThickness(1, 2, 3, 4),
            BorderBrush = "#102030",
            Background = "#DDEEFF",
            TextAlignment = FlowTextAlignment.Center
        };
        cell.Blocks.Add(new Paragraph("Cell"));
        row.Cells.Add(cell);
        rowGroup.Rows.Add(row);
        table.RowGroups.Add(rowGroup);
        document.Blocks.Add(table);

        var converter = new FlowDocumentConverter();
        var result = converter.Convert(document);

        var tableBlock = Assert.IsType<TableBlock>(result.Blocks[0]);
        var tableCell = tableBlock.Rows[0].Cells[0];
        Assert.NotNull(tableCell.Properties.Padding);
        Assert.Equal(6f, tableCell.Properties.Padding!.Value.Left);
        Assert.Equal(4f, tableCell.Properties.Padding!.Value.Top);
        Assert.NotNull(tableCell.Properties.ShadingColor);
        Assert.Equal(new ProEdit.Primitives.DocColor(221, 238, 255), tableCell.Properties.ShadingColor!.Value);
        Assert.NotNull(tableCell.Properties.Borders.Top);
        Assert.NotNull(tableCell.Properties.Borders.Bottom);
        Assert.NotNull(tableCell.Properties.Borders.Left);
        Assert.NotNull(tableCell.Properties.Borders.Right);
        Assert.Equal(2f, tableCell.Properties.Borders.Top!.Thickness);
        Assert.Equal(1f, tableCell.Properties.Borders.Left!.Thickness);
        Assert.Equal(3f, tableCell.Properties.Borders.Right!.Thickness);
        Assert.Equal(4f, tableCell.Properties.Borders.Bottom!.Thickness);

        var paragraph = Assert.IsType<ParagraphBlock>(tableCell.Blocks[0]);
        Assert.Equal(ParagraphAlignment.Center, paragraph.Properties.Alignment);
    }

    [Fact]
    public void ConvertsParagraphVisualPropertiesToDocumentModel()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph("Flow")
        {
            TextIndent = 24,
            KeepTogether = true,
            KeepWithNext = true,
            BreakPageBefore = true,
            LineHeight = 18,
            LineStackingStrategy = "Exactly",
            Background = "#F0E0D0",
            BorderThickness = new FlowThickness(1, 1, 1, 1),
            BorderBrush = "#223344",
            FlowDirection = "RightToLeft"
        };
        document.Blocks.Add(paragraph);

        var converter = new FlowDocumentConverter();
        var result = converter.Convert(document);

        var paragraphBlock = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        Assert.Equal(24f, paragraphBlock.Properties.FirstLineIndent);
        Assert.Equal(true, paragraphBlock.Properties.KeepLinesTogether);
        Assert.Equal(true, paragraphBlock.Properties.KeepWithNext);
        Assert.Equal(true, paragraphBlock.Properties.PageBreakBefore);
        Assert.Equal(18, paragraphBlock.Properties.LineSpacing);
        Assert.Equal(DocLineSpacingRule.Exactly, paragraphBlock.Properties.LineSpacingRule);
        Assert.Equal(true, paragraphBlock.Properties.Bidi);
        Assert.Equal(new ProEdit.Primitives.DocColor(240, 224, 208), paragraphBlock.Properties.ShadingColor);
        Assert.NotNull(paragraphBlock.Properties.Borders.Top);
        Assert.NotNull(paragraphBlock.Properties.Borders.Bottom);
        Assert.NotNull(paragraphBlock.Properties.Borders.Left);
        Assert.NotNull(paragraphBlock.Properties.Borders.Right);
    }

    [Fact]
    public void DisablesCellVisualExportWhenOptionIsOff()
    {
        var document = new FlowDocument();
        var table = new Table();
        var group = new TableRowGroup();
        var row = new TableRow();
        var cell = new TableCell
        {
            Padding = new FlowThickness(4, 4, 4, 4),
            BorderThickness = new FlowThickness(1, 1, 1, 1),
            BorderBrush = "#101010",
            Background = "#EFEFEF"
        };
        cell.Blocks.Add(new Paragraph("Cell"));
        row.Cells.Add(cell);
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        document.Blocks.Add(table);

        var converter = new FlowDocumentConverter(new FlowDocumentConverterOptions
        {
            ExportCellVisualProperties = false
        });
        var result = converter.Convert(document);

        var tableCell = Assert.IsType<TableBlock>(result.Blocks[0]).Rows[0].Cells[0];
        Assert.Null(tableCell.Properties.Padding);
        Assert.Null(tableCell.Properties.ShadingColor);
        Assert.Null(tableCell.Properties.Borders.Top);
        Assert.Null(tableCell.Properties.Borders.Bottom);
        Assert.Null(tableCell.Properties.Borders.Left);
        Assert.Null(tableCell.Properties.Borders.Right);
    }

    private sealed class EmbeddedTestChild;
}

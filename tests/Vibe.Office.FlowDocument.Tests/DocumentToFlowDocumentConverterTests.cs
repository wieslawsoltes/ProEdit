using System.Linq;
using Avalonia.Controls;
using Vibe.Office.Documents;
using Vibe.Office.FlowDocument;
using Vibe.Office.FlowDocument.Documents;
using Xunit;

namespace Vibe.Office.FlowDocument.Tests;

public sealed class DocumentToFlowDocumentConverterTests
{
    [Theory]
    [Trait("Category", "FlowRoundtrip")]
    [InlineData("core-structures")]
    [InlineData("nested-list")]
    [InlineData("embedded-block-marker")]
    public void RoundtripFixtureSet(string fixtureName)
    {
        switch (fixtureName)
        {
            case "core-structures":
                Roundtrip_PreservesCoreStructures_AndRestoresEmbeddedUiContainers();
                break;
            case "nested-list":
                Roundtrip_ReconstructsNestedLists_WithStartIndex();
                break;
            case "embedded-block-marker":
                ConvertsEmbeddedBlockShapeMarker_ToBlockUiContainer();
                break;
            default:
                throw new InvalidOperationException($"Unknown fixture name '{fixtureName}'.");
        }
    }

    [Fact]
    public void Roundtrip_PreservesCoreStructures_AndRestoresEmbeddedUiContainers()
    {
        var inlineButton = new Button { Content = "Inline", Width = 96, Height = 24 };
        var blockButton = new Button { Content = "Block", Width = 200, Height = 80 };

        var source = new FlowDocument
        {
            FontFamily = "Cascadia Mono",
            FontSize = 12,
            PageWidth = 800,
            PageHeight = 1000,
            PagePadding = new FlowThickness(64, 72, 64, 72),
            ColumnGap = 28
        };

        var paragraph = new Paragraph();
        var bold = new Bold();
        bold.Inlines.Add(new Run("Bold"));
        paragraph.Inlines.Add(bold);
        paragraph.Inlines.Add(new Run(" "));
        var hyperlink = new Hyperlink { NavigateUri = "https://example.com" };
        hyperlink.Inlines.Add(new Run("Link"));
        paragraph.Inlines.Add(hyperlink);
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run("Tail"));
        paragraph.Inlines.Add(new InlineUIContainer { Child = inlineButton });
        var figure = new Figure { Width = 180, Height = 90 };
        figure.Blocks.Add(new Paragraph("Figure body"));
        paragraph.Inlines.Add(figure);
        source.Blocks.Add(paragraph);

        var table = new Table();
        var rowGroup = new TableRowGroup();
        var row1 = new TableRow();
        var spanCell = new TableCell { RowSpan = 2 };
        spanCell.Blocks.Add(new Paragraph("Span"));
        row1.Cells.Add(spanCell);
        var row1Cell2 = new TableCell();
        row1Cell2.Blocks.Add(new Paragraph("R1C2"));
        row1.Cells.Add(row1Cell2);
        rowGroup.Rows.Add(row1);
        var row2 = new TableRow();
        var row2Cell = new TableCell();
        row2Cell.Blocks.Add(new Paragraph("R2C2"));
        row2.Cells.Add(row2Cell);
        rowGroup.Rows.Add(row2);
        table.RowGroups.Add(rowGroup);
        source.Blocks.Add(table);

        source.Blocks.Add(new BlockUIContainer { Child = blockButton });

        var toDocument = new FlowDocumentConverter(new FlowDocumentConverterOptions
        {
            EnableEmbeddedUiElements = true
        });
        var document = toDocument.Convert(source);
        var map = toDocument.EmbeddedUiElements.ToDictionary(item => item.Id, StringComparer.Ordinal);

        var toFlow = new DocumentToFlowDocumentConverter(new DocumentToFlowDocumentConverterOptions
        {
            EmbeddedUiElementsById = map
        });
        var roundtrip = toFlow.Convert(document);

        Assert.Equal(3, roundtrip.Blocks.Count);
        Assert.Equal("Cascadia Mono", roundtrip.FontFamily);
        Assert.Equal(800d, roundtrip.PageWidth);
        Assert.Equal(1000d, roundtrip.PageHeight);

        var roundtripParagraph = Assert.IsType<Paragraph>(roundtrip.Blocks[0]);
        Assert.Contains(roundtripParagraph.Inlines, inline => inline is InlineUIContainer);
        Assert.Contains(roundtripParagraph.Inlines, inline => inline is Figure or Floater);
        var restoredInlineContainer = roundtripParagraph.Inlines.OfType<InlineUIContainer>().First();
        Assert.Same(inlineButton, restoredInlineContainer.Child);

        var roundtripTable = Assert.IsType<Table>(roundtrip.Blocks[1]);
        var firstRowCell = roundtripTable.RowGroups[0].Rows[0].Cells[0];
        Assert.Equal(2, firstRowCell.RowSpan);

        var restoredBlockUi = Assert.IsType<BlockUIContainer>(roundtrip.Blocks[2]);
        Assert.Same(blockButton, restoredBlockUi.Child);
    }

    [Fact]
    public void Roundtrip_ReconstructsNestedLists_WithStartIndex()
    {
        var source = new FlowDocument();
        var topList = new List
        {
            MarkerStyle = FlowListMarkerStyle.Decimal,
            StartIndex = 3
        };
        var firstItem = new ListItem();
        firstItem.Blocks.Add(new Paragraph("Top item"));
        var nested = new List { MarkerStyle = FlowListMarkerStyle.Disc };
        var nestedItem = new ListItem();
        nestedItem.Blocks.Add(new Paragraph("Nested item"));
        nested.ListItems.Add(nestedItem);
        firstItem.Blocks.Add(nested);
        topList.ListItems.Add(firstItem);
        source.Blocks.Add(topList);

        var toDocument = new FlowDocumentConverter();
        var document = toDocument.Convert(source);

        var toFlow = new DocumentToFlowDocumentConverter();
        var roundtrip = toFlow.Convert(document);

        var list = Assert.IsType<List>(roundtrip.Blocks[0]);
        Assert.Equal(FlowListMarkerStyle.Decimal, list.MarkerStyle);
        Assert.Equal(3, list.StartIndex);
        Assert.NotEmpty(list.ListItems);
        Assert.Contains(list.ListItems[0].Blocks, block => block is List);
    }

    [Fact]
    public void ConvertsEmbeddedBlockShapeMarker_ToBlockUiContainer()
    {
        var blockMarkerId = "42";
        var blockChild = new Button { Content = "Embedded block" };

        var document = new Document();
        document.Blocks.Clear();
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new ShapeInline(200, 100)
        {
            Name = $"{FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix}{blockMarkerId}"
        });
        document.Blocks.Add(paragraph);

        var converter = new DocumentToFlowDocumentConverter(new DocumentToFlowDocumentConverterOptions
        {
            EmbeddedUiElementsById = new Dictionary<string, EmbeddedFlowUiElement>(StringComparer.Ordinal)
            {
                [blockMarkerId] = new EmbeddedFlowUiElement(blockMarkerId, blockChild, isInline: false)
            }
        });

        var flow = converter.Convert(document);
        var blockUi = Assert.IsType<BlockUIContainer>(flow.Blocks[0]);
        Assert.Same(blockChild, blockUi.Child);
    }

    [Fact]
    public void TryConvertTopLevelBlock_ConvertsPlainParagraph()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Alpha"));
        document.Blocks.Add(new ParagraphBlock("Beta"));

        var converter = new DocumentToFlowDocumentConverter();

        Assert.True(converter.TryConvertTopLevelBlock(document, 1, out var block));
        var paragraph = Assert.IsType<Paragraph>(block);
        var run = Assert.IsType<Run>(paragraph.Inlines[0]);
        Assert.Equal("Beta", run.Text);
    }

    [Fact]
    public void TryConvertTopLevelBlock_ReturnsFalseForListParagraph()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Item", new ListInfo(ListKind.Bullet)));

        var converter = new DocumentToFlowDocumentConverter();

        Assert.False(converter.TryConvertTopLevelBlock(document, 0, out _));
    }
}

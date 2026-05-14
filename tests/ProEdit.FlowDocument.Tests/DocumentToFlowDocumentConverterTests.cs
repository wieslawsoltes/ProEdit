using System.Linq;
using Avalonia.Controls;
using ProEdit.Documents;
using ProEdit.FlowDocument;
using ProEdit.FlowDocument.Documents;
using Xunit;

namespace ProEdit.FlowDocument.Tests;

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
    public void ConvertsEmbeddedFloatingShapeMarker_ToAnchoredUiContainer()
    {
        var markerId = "77";
        var floatingChild = new Button { Content = "Floating block" };

        var document = new Document();
        document.Blocks.Clear();

        var paragraph = new ParagraphBlock();
        var floatingShape = new ShapeInline(180, 72)
        {
            Name = $"{FlowDocumentConverterOptions.DefaultEmbeddedUiShapePrefix}{markerId}"
        };
        paragraph.FloatingObjects.Add(new FloatingObject(floatingShape));
        document.Blocks.Add(paragraph);

        var converter = new DocumentToFlowDocumentConverter(new DocumentToFlowDocumentConverterOptions
        {
            EmbeddedUiElementsById = new Dictionary<string, EmbeddedFlowUiElement>(StringComparer.Ordinal)
            {
                [markerId] = new EmbeddedFlowUiElement(markerId, floatingChild, isInline: false)
            }
        });

        var flow = converter.Convert(document);
        var flowParagraph = Assert.IsType<Paragraph>(Assert.Single(flow.Blocks));
        var figure = Assert.IsType<Figure>(Assert.Single(flowParagraph.Inlines));
        var blockUi = Assert.IsType<BlockUIContainer>(Assert.Single(figure.Blocks));
        Assert.Same(floatingChild, blockUi.Child);
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

    [Fact]
    public void ConvertsCharacterStyleOnlyRun_ToVisibleFlowFormatting()
    {
        var document = new Document();
        document.Blocks.Clear();
        var characterStyle = new CharacterStyleDefinition("StrongEmphasis");
        characterStyle.RunProperties.FontWeight = DocFontWeight.Bold;
        characterStyle.RunProperties.FontStyle = DocFontStyle.Italic;
        document.Styles.CharacterStyles[characterStyle.Id] = characterStyle;

        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new RunInline("Styled") { StyleId = characterStyle.Id });
        document.Blocks.Add(paragraph);

        var converter = new DocumentToFlowDocumentConverter();
        var flow = converter.Convert(document);

        var flowParagraph = Assert.IsType<Paragraph>(flow.Blocks[0]);
        var span = Assert.IsType<Span>(flowParagraph.Inlines[0]);
        Assert.Equal(FlowFontWeight.Bold, span.FontWeight);
        Assert.Equal(FlowFontStyle.Italic, span.FontStyle);
        var run = Assert.IsType<Run>(span.Inlines[0]);
        Assert.Equal("Styled", run.Text);
    }

    [Fact]
    public void ConvertsParagraphStyleInheritance_ToParagraphLevelFlowProperties()
    {
        var document = new Document();
        document.Blocks.Clear();
        var heading = new ParagraphStyleDefinition("HeadingOne");
        heading.ParagraphProperties.Alignment = ParagraphAlignment.Center;
        heading.ParagraphProperties.PageBreakBefore = true;
        heading.RunProperties.FontStyle = DocFontStyle.Italic;
        heading.RunProperties.FontWeight = DocFontWeight.Bold;
        document.Styles.ParagraphStyles[heading.Id] = heading;

        var paragraph = new ParagraphBlock("Heading text") { StyleId = heading.Id };
        document.Blocks.Add(paragraph);

        var converter = new DocumentToFlowDocumentConverter();
        var flow = converter.Convert(document);

        var flowParagraph = Assert.IsType<Paragraph>(flow.Blocks[0]);
        Assert.Equal(FlowTextAlignment.Center, flowParagraph.TextAlignment);
        Assert.Equal(true, flowParagraph.BreakPageBefore);
        Assert.Equal(FlowFontStyle.Italic, flowParagraph.FontStyle);
        Assert.Equal(FlowFontWeight.Bold, flowParagraph.FontWeight);
    }

    [Fact]
    public void ConvertsTableStyleConditions_ToFlowTableCellVisuals()
    {
        var document = new Document();
        document.Blocks.Clear();

        var tableStyle = new TableStyleDefinition("TableGrid");
        tableStyle.TableProperties.Look = new TableLook
        {
            FirstRow = true,
            BandedRows = true
        };
        tableStyle.CellProperties.Padding = new ProEdit.Primitives.DocThickness(8f, 4f, 8f, 4f);
        tableStyle.CellProperties.Borders.Bottom = new BorderLine
        {
            Thickness = 1f,
            Color = ProEdit.Primitives.DocColor.Black
        };
        var firstRowCondition = new TableStyleConditionProperties();
        firstRowCondition.CellProperties.ShadingColor = new ProEdit.Primitives.DocColor(230, 240, 255);
        tableStyle.Conditions[TableStyleCondition.FirstRow] = firstRowCondition;
        document.Styles.TableStyles[tableStyle.Id] = tableStyle;

        var table = new TableBlock
        {
            StyleId = tableStyle.Id
        };
        var row1 = new ProEdit.Documents.TableRow();
        var row1Cell = new ProEdit.Documents.TableCell();
        row1Cell.Blocks.Add(new ParagraphBlock("Header"));
        row1.Cells.Add(row1Cell);
        table.Rows.Add(row1);

        var row2 = new ProEdit.Documents.TableRow();
        var row2Cell = new ProEdit.Documents.TableCell();
        row2Cell.Blocks.Add(new ParagraphBlock("Body"));
        row2.Cells.Add(row2Cell);
        table.Rows.Add(row2);
        document.Blocks.Add(table);

        var converter = new DocumentToFlowDocumentConverter();
        var flow = converter.Convert(document);

        var flowTable = Assert.IsType<Table>(flow.Blocks[0]);
        var headerCell = flowTable.RowGroups[0].Rows[0].Cells[0];
        var bodyCell = flowTable.RowGroups[0].Rows[1].Cells[0];

        Assert.False(headerCell.Padding.IsEmpty);
        Assert.False(headerCell.BorderThickness.IsEmpty);
        Assert.NotNull(headerCell.BorderBrush);
        Assert.Equal("#E6F0FF", headerCell.Background);
        Assert.NotEqual(headerCell.Background, bodyCell.Background);
    }
}

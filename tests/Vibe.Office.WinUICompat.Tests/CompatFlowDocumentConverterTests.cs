using Vibe.Office.WinUICompat.Converters;
using Vibe.Office.WinUICompat.Documents;
using Vibe.Office.WinUICompat.Text;
using Xunit;

namespace Vibe.Office.WinUICompat.Tests;

public sealed class CompatFlowDocumentConverterTests
{
    [Fact]
    public void ToFlowDocument_ConvertsParagraphRun()
    {
        var source = new RichTextDocument();
        source.Blocks.Add(new Paragraph("hello"));
        var converter = new CompatFlowDocumentConverter();

        var flow = converter.ToFlowDocument(source);

        var paragraph = Assert.IsType<Vibe.Office.FlowDocument.Paragraph>(Assert.Single(flow.Blocks));
        var run = Assert.IsType<Vibe.Office.FlowDocument.Run>(Assert.Single(paragraph.Inlines));
        Assert.Equal("hello", run.Text);
    }

    [Fact]
    public void FromFlowDocument_ConvertsParagraphRun()
    {
        var flow = new Vibe.Office.FlowDocument.FlowDocument();
        var paragraph = new Vibe.Office.FlowDocument.Paragraph();
        paragraph.Inlines.Add(new Vibe.Office.FlowDocument.Run("hello"));
        flow.Blocks.Add(paragraph);

        var converter = new CompatFlowDocumentConverter();
        var compat = converter.FromFlowDocument(flow);

        var compatParagraph = Assert.IsType<Paragraph>(Assert.Single(compat.Blocks));
        var compatRun = Assert.IsType<Run>(Assert.Single(compatParagraph.Inlines));
        Assert.Equal("hello", compatRun.Text);
    }

    [Fact]
    public void ToFlowDocument_PreservesIntrinsicInlineStyles()
    {
        var source = new RichTextDocument();
        var paragraph = new Paragraph();

        var bold = new Bold();
        bold.Inlines.Add(new Run("bold"));
        paragraph.Inlines.Add(bold);

        var italic = new Italic();
        italic.Inlines.Add(new Run("italic"));
        paragraph.Inlines.Add(italic);

        var underline = new Underline();
        underline.Inlines.Add(new Run("underline"));
        paragraph.Inlines.Add(underline);

        source.Blocks.Add(paragraph);

        var converter = new CompatFlowDocumentConverter();
        var flow = converter.ToFlowDocument(source);

        var flowParagraph = Assert.IsType<Vibe.Office.FlowDocument.Paragraph>(Assert.Single(flow.Blocks));
        var flowBold = Assert.IsType<Vibe.Office.FlowDocument.Bold>(flowParagraph.Inlines[0]);
        var flowItalic = Assert.IsType<Vibe.Office.FlowDocument.Italic>(flowParagraph.Inlines[1]);
        var flowUnderline = Assert.IsType<Vibe.Office.FlowDocument.Underline>(flowParagraph.Inlines[2]);

        Assert.Equal(Vibe.Office.FlowDocument.FlowFontWeight.Bold, flowBold.FontWeight);
        Assert.Equal(Vibe.Office.FlowDocument.FlowFontStyle.Italic, flowItalic.FontStyle);
        Assert.Equal(Vibe.Office.FlowDocument.FlowTextDecorations.Underline, flowUnderline.TextDecorations);
    }

    [Fact]
    public void ToFlowDocument_UsesExplicitInlineStyleOverrides()
    {
        var source = new RichTextDocument();
        var paragraph = new Paragraph();
        var bold = new Bold
        {
            FontWeight = nameof(Vibe.Office.FlowDocument.FlowFontWeight.Normal)
        };
        bold.Inlines.Add(new Run("override"));
        paragraph.Inlines.Add(bold);
        source.Blocks.Add(paragraph);

        var converter = new CompatFlowDocumentConverter();
        var flow = converter.ToFlowDocument(source);

        var flowParagraph = Assert.IsType<Vibe.Office.FlowDocument.Paragraph>(Assert.Single(flow.Blocks));
        var flowBold = Assert.IsType<Vibe.Office.FlowDocument.Bold>(Assert.Single(flowParagraph.Inlines));
        Assert.Equal(Vibe.Office.FlowDocument.FlowFontWeight.Normal, flowBold.FontWeight);
    }

    [Fact]
    public void ToFlowDocument_PreservesUpperRomanListStyle()
    {
        var source = new RichTextDocument();
        var list = new List
        {
            MarkerStyle = nameof(Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman),
            StartIndex = 3
        };
        list.ListItems.Add(new ListItem { Blocks = { new Paragraph("one") } });
        source.Blocks.Add(list);

        var converter = new CompatFlowDocumentConverter();
        var flow = converter.ToFlowDocument(source);

        var flowList = Assert.IsType<Vibe.Office.FlowDocument.List>(Assert.Single(flow.Blocks));
        Assert.Equal(Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman, flowList.MarkerStyle);
        Assert.Equal(3, flowList.StartIndex);
    }

    [Fact]
    public void FlowToCompatToFlow_RetainsAvaloniaRichFormatting()
    {
        var source = new Vibe.Office.FlowDocument.FlowDocument();
        var paragraph = new Vibe.Office.FlowDocument.Paragraph();

        var bold = new Vibe.Office.FlowDocument.Bold();
        bold.Inlines.Add(new Vibe.Office.FlowDocument.Run("bold"));
        paragraph.Inlines.Add(bold);

        var italic = new Vibe.Office.FlowDocument.Italic();
        italic.Inlines.Add(new Vibe.Office.FlowDocument.Run("italic"));
        paragraph.Inlines.Add(italic);

        var underline = new Vibe.Office.FlowDocument.Underline();
        underline.Inlines.Add(new Vibe.Office.FlowDocument.Run("underline"));
        paragraph.Inlines.Add(underline);
        source.Blocks.Add(paragraph);

        var list = new Vibe.Office.FlowDocument.List
        {
            MarkerStyle = Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman,
            StartIndex = 3
        };
        var item = new Vibe.Office.FlowDocument.ListItem();
        item.Blocks.Add(new Vibe.Office.FlowDocument.Paragraph("item"));
        list.ListItems.Add(item);
        source.Blocks.Add(list);

        var converter = new CompatFlowDocumentConverter();
        var compat = converter.FromFlowDocument(source);
        var roundtrip = converter.ToFlowDocument(compat);

        var roundtripParagraph = Assert.IsType<Vibe.Office.FlowDocument.Paragraph>(roundtrip.Blocks[0]);
        var roundtripBold = Assert.IsType<Vibe.Office.FlowDocument.Bold>(roundtripParagraph.Inlines[0]);
        var roundtripItalic = Assert.IsType<Vibe.Office.FlowDocument.Italic>(roundtripParagraph.Inlines[1]);
        var roundtripUnderline = Assert.IsType<Vibe.Office.FlowDocument.Underline>(roundtripParagraph.Inlines[2]);

        Assert.Equal(Vibe.Office.FlowDocument.FlowFontWeight.Bold, roundtripBold.FontWeight);
        Assert.Equal(Vibe.Office.FlowDocument.FlowFontStyle.Italic, roundtripItalic.FontStyle);
        Assert.Equal(Vibe.Office.FlowDocument.FlowTextDecorations.Underline, roundtripUnderline.TextDecorations);

        var roundtripList = Assert.IsType<Vibe.Office.FlowDocument.List>(roundtrip.Blocks[1]);
        Assert.Equal(Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman, roundtripList.MarkerStyle);
        Assert.Equal(3, roundtripList.StartIndex);
    }

    [Fact]
    public void FlowToCompatThroughEditorRoundtrip_RetainsRichFormatting()
    {
        var source = new Vibe.Office.FlowDocument.FlowDocument();
        var paragraph = new Vibe.Office.FlowDocument.Paragraph();

        var bold = new Vibe.Office.FlowDocument.Bold();
        bold.Inlines.Add(new Vibe.Office.FlowDocument.Run("bold"));
        paragraph.Inlines.Add(bold);
        source.Blocks.Add(paragraph);

        var list = new Vibe.Office.FlowDocument.List
        {
            MarkerStyle = Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman,
            StartIndex = 3
        };
        var listItem = new Vibe.Office.FlowDocument.ListItem();
        listItem.Blocks.Add(new Vibe.Office.FlowDocument.Paragraph("item"));
        list.ListItems.Add(listItem);
        source.Blocks.Add(list);

        var table = new Vibe.Office.FlowDocument.Table();
        var group = new Vibe.Office.FlowDocument.TableRowGroup();
        var row1 = new Vibe.Office.FlowDocument.TableRow();
        row1.Cells.Add(new Vibe.Office.FlowDocument.TableCell
        {
            RowSpan = 2,
            Blocks = { new Vibe.Office.FlowDocument.Paragraph("A") }
        });
        row1.Cells.Add(new Vibe.Office.FlowDocument.TableCell
        {
            Blocks = { new Vibe.Office.FlowDocument.Paragraph("B") }
        });
        group.Rows.Add(row1);

        var row2 = new Vibe.Office.FlowDocument.TableRow();
        row2.Cells.Add(new Vibe.Office.FlowDocument.TableCell
        {
            Blocks = { new Vibe.Office.FlowDocument.Paragraph("C") }
        });
        group.Rows.Add(row2);
        table.RowGroups.Add(group);
        source.Blocks.Add(table);

        var converter = new CompatFlowDocumentConverter();
        var compat = converter.FromFlowDocument(source);

        var editorDocument = new RichEditTextDocument();
        editorDocument.SetDocument(compat);
        var snapshot = editorDocument.CreateEditorSnapshotDocument();

        var roundtrip = converter.ToFlowDocument(snapshot);
        var roundtripParagraph = Assert.IsType<Vibe.Office.FlowDocument.Paragraph>(roundtrip.Blocks[0]);
        var roundtripInlineSpan = Assert.IsType<Vibe.Office.FlowDocument.Span>(Assert.Single(roundtripParagraph.Inlines));
        Assert.Equal(Vibe.Office.FlowDocument.FlowFontWeight.Bold, roundtripInlineSpan.FontWeight);

        var roundtripList = Assert.IsType<Vibe.Office.FlowDocument.List>(roundtrip.Blocks[1]);
        Assert.Equal(Vibe.Office.FlowDocument.FlowListMarkerStyle.UpperRoman, roundtripList.MarkerStyle);
        Assert.Equal(3, roundtripList.StartIndex);

        var roundtripTable = Assert.IsType<Vibe.Office.FlowDocument.Table>(roundtrip.Blocks[2]);
        var roundtripRow1 = roundtripTable.RowGroups[0].Rows[0];
        var roundtripRow2 = roundtripTable.RowGroups[0].Rows[1];
        Assert.Equal(2, roundtripRow1.Cells[0].RowSpan);
        Assert.Single(roundtripRow2.Cells);
    }

    [Fact]
    public void ToFlowDocument_MapsDocumentAndParagraphLayoutProperties()
    {
        var source = new RichTextDocument
        {
            FontFamily = "Calibri",
            FontSize = 15,
            FontWeight = nameof(Vibe.Office.FlowDocument.FlowFontWeight.Bold),
            FontStyle = nameof(Vibe.Office.FlowDocument.FlowFontStyle.Italic),
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new Thickness(72, 72, 72, 72),
            ColumnWidth = 280,
            ColumnGap = 24,
            TextAlignment = nameof(Vibe.Office.FlowDocument.FlowTextAlignment.Justify),
            ColumnRuleBrush = "#8899AA",
            ColumnRuleWidth = 2,
            FlowDirection = "RightToLeft",
            IsColumnWidthFlexible = true,
            IsHyphenationEnabled = true,
            IsOptimalParagraphEnabled = true,
            LineHeight = 20,
            LineStackingStrategy = "Exact",
            MaxPageHeight = 1200,
            MaxPageWidth = 900,
            MinPageHeight = 600,
            MinPageWidth = 500
        };

        var paragraph = new Paragraph("layout")
        {
            Margin = new Thickness(8, 10, 12, 14),
            Padding = new Thickness(2, 3, 4, 5),
            BorderThickness = new Thickness(1, 1, 1, 1),
            BorderBrush = "#224466",
            TextAlignment = nameof(Vibe.Office.FlowDocument.FlowTextAlignment.Center),
            LineHeight = 18,
            FlowDirection = "LeftToRight",
            LineStackingStrategy = "MaxHeight",
            BreakColumnBefore = true,
            BreakPageBefore = true,
            ClearFloaters = "Both",
            IsHyphenationEnabled = true,
            KeepTogether = true,
            KeepWithNext = true,
            MinOrphanLines = 2,
            MinWidowLines = 2,
            TextIndent = 16,
            TextDecorations = nameof(Vibe.Office.FlowDocument.FlowTextDecorations.Underline)
        };
        source.Blocks.Add(paragraph);

        var converter = new CompatFlowDocumentConverter();
        var flow = converter.ToFlowDocument(source);

        Assert.Equal("Calibri", flow.FontFamily);
        Assert.Equal(15, flow.FontSize);
        Assert.Equal(Vibe.Office.FlowDocument.FlowFontWeight.Bold, flow.FontWeight);
        Assert.Equal(Vibe.Office.FlowDocument.FlowFontStyle.Italic, flow.FontStyle);
        Assert.Equal(816, flow.PageWidth);
        Assert.Equal(1056, flow.PageHeight);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(72, 72, 72, 72), flow.PagePadding);
        Assert.Equal(280, flow.ColumnWidth);
        Assert.Equal(24, flow.ColumnGap);
        Assert.Equal(Vibe.Office.FlowDocument.FlowTextAlignment.Justify, flow.TextAlignment);
        Assert.Equal("#8899AA", flow.ColumnRuleBrush);
        Assert.Equal(2, flow.ColumnRuleWidth);
        Assert.Equal("RightToLeft", flow.FlowDirection);
        Assert.True(flow.IsColumnWidthFlexible);
        Assert.True(flow.IsHyphenationEnabled);
        Assert.True(flow.IsOptimalParagraphEnabled);
        Assert.Equal(20, flow.LineHeight);
        Assert.Equal("Exact", flow.LineStackingStrategy);
        Assert.Equal(1200, flow.MaxPageHeight);
        Assert.Equal(900, flow.MaxPageWidth);
        Assert.Equal(600, flow.MinPageHeight);
        Assert.Equal(500, flow.MinPageWidth);

        var flowParagraph = Assert.IsType<Vibe.Office.FlowDocument.Paragraph>(Assert.Single(flow.Blocks));
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(8, 10, 12, 14), flowParagraph.Margin);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(2, 3, 4, 5), flowParagraph.Padding);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(1, 1, 1, 1), flowParagraph.BorderThickness);
        Assert.Equal("#224466", flowParagraph.BorderBrush);
        Assert.Equal(Vibe.Office.FlowDocument.FlowTextAlignment.Center, flowParagraph.TextAlignment);
        Assert.Equal(18, flowParagraph.LineHeight);
        Assert.Equal("LeftToRight", flowParagraph.FlowDirection);
        Assert.Equal("MaxHeight", flowParagraph.LineStackingStrategy);
        Assert.True(flowParagraph.BreakColumnBefore);
        Assert.True(flowParagraph.BreakPageBefore);
        Assert.Equal("Both", flowParagraph.ClearFloaters);
        Assert.True(flowParagraph.IsHyphenationEnabled);
        Assert.True(flowParagraph.KeepTogether);
        Assert.True(flowParagraph.KeepWithNext);
        Assert.Equal(2, flowParagraph.MinOrphanLines);
        Assert.Equal(2, flowParagraph.MinWidowLines);
        Assert.Equal(16, flowParagraph.TextIndent);
        Assert.Equal(Vibe.Office.FlowDocument.FlowTextDecorations.Underline, flowParagraph.TextDecorations);
    }

    [Fact]
    public void FlowToCompatToFlow_RetainsListItemAndTableCellVisualProperties()
    {
        var source = new Vibe.Office.FlowDocument.FlowDocument
        {
            PagePadding = new Vibe.Office.FlowDocument.FlowThickness(40, 30, 20, 10),
            ColumnWidth = 320,
            ColumnGap = 18
        };

        var list = new Vibe.Office.FlowDocument.List
        {
            MarkerStyle = Vibe.Office.FlowDocument.FlowListMarkerStyle.Decimal,
            StartIndex = 4,
            MarkerOffset = 24
        };
        var listItem = new Vibe.Office.FlowDocument.ListItem
        {
            Margin = new Vibe.Office.FlowDocument.FlowThickness(5, 6, 7, 8),
            Padding = new Vibe.Office.FlowDocument.FlowThickness(1, 2, 3, 4),
            BorderThickness = new Vibe.Office.FlowDocument.FlowThickness(1, 1, 1, 1),
            BorderBrush = "#445566",
            FlowDirection = "RightToLeft",
            LineHeight = 19,
            LineStackingStrategy = "MaxHeight",
            TextAlignment = Vibe.Office.FlowDocument.FlowTextAlignment.Right
        };
        listItem.Blocks.Add(new Vibe.Office.FlowDocument.Paragraph("item"));
        list.ListItems.Add(listItem);
        source.Blocks.Add(list);

        var table = new Vibe.Office.FlowDocument.Table();
        var group = new Vibe.Office.FlowDocument.TableRowGroup();
        var row = new Vibe.Office.FlowDocument.TableRow();
        row.Cells.Add(new Vibe.Office.FlowDocument.TableCell
        {
            ColumnSpan = 2,
            RowSpan = 3,
            Padding = new Vibe.Office.FlowDocument.FlowThickness(4, 5, 6, 7),
            BorderThickness = new Vibe.Office.FlowDocument.FlowThickness(2, 2, 2, 2),
            BorderBrush = "#778899",
            FlowDirection = "RightToLeft",
            LineHeight = 21,
            LineStackingStrategy = "Exact",
            TextAlignment = Vibe.Office.FlowDocument.FlowTextAlignment.Justify,
            Blocks = { new Vibe.Office.FlowDocument.Paragraph("cell") }
        });
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        source.Blocks.Add(table);

        var converter = new CompatFlowDocumentConverter();
        var compat = converter.FromFlowDocument(source);
        var roundtrip = converter.ToFlowDocument(compat);

        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(40, 30, 20, 10), roundtrip.PagePadding);
        Assert.Equal(320, roundtrip.ColumnWidth);
        Assert.Equal(18, roundtrip.ColumnGap);

        var roundtripList = Assert.IsType<Vibe.Office.FlowDocument.List>(roundtrip.Blocks[0]);
        Assert.Equal(Vibe.Office.FlowDocument.FlowListMarkerStyle.Decimal, roundtripList.MarkerStyle);
        Assert.Equal(4, roundtripList.StartIndex);
        Assert.Equal(24, roundtripList.MarkerOffset);
        var roundtripItem = Assert.Single(roundtripList.ListItems);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(5, 6, 7, 8), roundtripItem.Margin);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(1, 2, 3, 4), roundtripItem.Padding);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(1, 1, 1, 1), roundtripItem.BorderThickness);
        Assert.Equal("#445566", roundtripItem.BorderBrush);
        Assert.Equal("RightToLeft", roundtripItem.FlowDirection);
        Assert.Equal(19, roundtripItem.LineHeight);
        Assert.Equal("MaxHeight", roundtripItem.LineStackingStrategy);
        Assert.Equal(Vibe.Office.FlowDocument.FlowTextAlignment.Right, roundtripItem.TextAlignment);

        var roundtripTable = Assert.IsType<Vibe.Office.FlowDocument.Table>(roundtrip.Blocks[1]);
        var roundtripCell = Assert.Single(Assert.Single(Assert.Single(roundtripTable.RowGroups).Rows).Cells);
        Assert.Equal(2, roundtripCell.ColumnSpan);
        Assert.Equal(3, roundtripCell.RowSpan);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(4, 5, 6, 7), roundtripCell.Padding);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(2, 2, 2, 2), roundtripCell.BorderThickness);
        Assert.Equal("#778899", roundtripCell.BorderBrush);
        Assert.Equal("RightToLeft", roundtripCell.FlowDirection);
        Assert.Equal(21, roundtripCell.LineHeight);
        Assert.Equal("Exact", roundtripCell.LineStackingStrategy);
        Assert.Equal(Vibe.Office.FlowDocument.FlowTextAlignment.Justify, roundtripCell.TextAlignment);
    }

    [Fact]
    public void FlowToCompatToFlow_RetainsAnchoredInlineContent()
    {
        var source = new Vibe.Office.FlowDocument.FlowDocument();
        var paragraph = new Vibe.Office.FlowDocument.Paragraph();

        var figure = new Vibe.Office.FlowDocument.Figure
        {
            Width = 240,
            Height = 120,
            HorizontalAnchor = "PageLeft",
            VerticalAnchor = "ParagraphTop",
            HorizontalOffset = 12,
            VerticalOffset = 8,
            WrapDirection = "Both",
            Margin = new Vibe.Office.FlowDocument.FlowThickness(2, 3, 4, 5),
            Padding = new Vibe.Office.FlowDocument.FlowThickness(6, 7, 8, 9),
            BorderThickness = new Vibe.Office.FlowDocument.FlowThickness(1, 1, 1, 1),
            BorderBrush = "#345678",
            TextAlignment = Vibe.Office.FlowDocument.FlowTextAlignment.Center,
            LineHeight = 17,
            LineStackingStrategy = "Exact"
        };
        figure.Blocks.Add(new Vibe.Office.FlowDocument.Paragraph("Figure body"));
        paragraph.Inlines.Add(figure);

        var floater = new Vibe.Office.FlowDocument.Floater
        {
            Width = 220,
            HorizontalAlignment = "Right"
        };
        floater.Blocks.Add(new Vibe.Office.FlowDocument.Paragraph("Floater body"));
        paragraph.Inlines.Add(floater);
        source.Blocks.Add(paragraph);

        var converter = new CompatFlowDocumentConverter();
        var compat = converter.FromFlowDocument(source);
        var roundtrip = converter.ToFlowDocument(compat);

        var roundtripParagraph = Assert.IsType<Vibe.Office.FlowDocument.Paragraph>(Assert.Single(roundtrip.Blocks));
        Assert.Equal(2, roundtripParagraph.Inlines.Count);

        var roundtripFigure = Assert.IsType<Vibe.Office.FlowDocument.Figure>(roundtripParagraph.Inlines[0]);
        Assert.Equal(240, roundtripFigure.Width);
        Assert.Equal(120, roundtripFigure.Height);
        Assert.Equal("PageLeft", roundtripFigure.HorizontalAnchor);
        Assert.Equal("ParagraphTop", roundtripFigure.VerticalAnchor);
        Assert.Equal(12, roundtripFigure.HorizontalOffset);
        Assert.Equal(8, roundtripFigure.VerticalOffset);
        Assert.Equal("Both", roundtripFigure.WrapDirection);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(2, 3, 4, 5), roundtripFigure.Margin);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(6, 7, 8, 9), roundtripFigure.Padding);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(1, 1, 1, 1), roundtripFigure.BorderThickness);
        Assert.Equal("#345678", roundtripFigure.BorderBrush);
        Assert.Equal(Vibe.Office.FlowDocument.FlowTextAlignment.Center, roundtripFigure.TextAlignment);
        Assert.Equal(17, roundtripFigure.LineHeight);
        Assert.Equal("Exact", roundtripFigure.LineStackingStrategy);
        Assert.Single(roundtripFigure.Blocks);

        var roundtripFloater = Assert.IsType<Vibe.Office.FlowDocument.Floater>(roundtripParagraph.Inlines[1]);
        Assert.Equal(220, roundtripFloater.Width);
        Assert.Equal("Right", roundtripFloater.HorizontalAlignment);
        Assert.Single(roundtripFloater.Blocks);
    }

    [Fact]
    public void FlowToCompatToFlow_RetainsSectionHierarchy()
    {
        var source = new Vibe.Office.FlowDocument.FlowDocument();
        var section = new Vibe.Office.FlowDocument.Section
        {
            HasTrailingParagraphBreakOnPaste = true,
            Margin = new Vibe.Office.FlowDocument.FlowThickness(1, 2, 3, 4),
            BorderBrush = "#112233",
            BorderThickness = new Vibe.Office.FlowDocument.FlowThickness(1, 1, 1, 1)
        };
        section.Blocks.Add(new Vibe.Office.FlowDocument.Paragraph("inside section"));
        source.Blocks.Add(section);

        var converter = new CompatFlowDocumentConverter();
        var compat = converter.FromFlowDocument(source);
        var roundtrip = converter.ToFlowDocument(compat);

        var roundtripSection = Assert.IsType<Vibe.Office.FlowDocument.Section>(Assert.Single(roundtrip.Blocks));
        Assert.True(roundtripSection.HasTrailingParagraphBreakOnPaste);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(1, 2, 3, 4), roundtripSection.Margin);
        Assert.Equal("#112233", roundtripSection.BorderBrush);
        Assert.Equal(new Vibe.Office.FlowDocument.FlowThickness(1, 1, 1, 1), roundtripSection.BorderThickness);
        Assert.IsType<Vibe.Office.FlowDocument.Paragraph>(Assert.Single(roundtripSection.Blocks));
    }
}

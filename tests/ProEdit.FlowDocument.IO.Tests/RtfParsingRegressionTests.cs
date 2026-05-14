using System.Text;
using System.Linq;
using ProEdit.Documents;
using ProEdit.FlowDocument.IO;
using ProEdit.Primitives;
using DocumentTableCell = ProEdit.Documents.TableCell;
using DocumentTableRow = ProEdit.Documents.TableRow;
using Xunit;

namespace ProEdit.FlowDocument.IO.Tests;

public sealed class RtfParsingRegressionTests
{
    [Fact]
    public async Task LoadRtf_DecodesAnsiCodepageBytesFromFile()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "ansi-bytes.rtf");

        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes(@"{\rtf1\ansi\ansicpg1252 This "));
        bytes.Add(0x93);
        bytes.AddRange(Encoding.ASCII.GetBytes("quote"));
        bytes.Add(0x94);
        bytes.AddRange(Encoding.ASCII.GetBytes(@"\par}"));
        await File.WriteAllBytesAsync(path, bytes.ToArray());

        var service = new FlowDocumentFileConversionService();
        var loaded = await service.LoadAsync(path);

        var text = ExtractText(loaded);
        Assert.Contains("“quote”", text);
    }

    [Fact]
    public async Task LoadRtf_DecodesAnsiCodepage1250BytesFromFile()
    {
        using var fixture = new TempDirectoryFixture();
        var path = Path.Combine(fixture.Path, "ansi-1250-bytes.rtf");

        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes(@"{\rtf1\ansi\ansicpg1250 "));
        bytes.Add(0xB9); // cp1250: "ą"
        bytes.AddRange(Encoding.ASCII.GetBytes(@"\par}"));
        await File.WriteAllBytesAsync(path, bytes.ToArray());

        var service = new FlowDocumentFileConversionService();
        var loaded = await service.LoadAsync(path);

        var text = ExtractText(loaded);
        Assert.Contains("ą", text);
    }

    [Fact]
    public void ParseRtf_ColorTableWithoutDefault_FirstBlackColorIsPreserved()
    {
        const string rtf = @"{\rtf1\ansi{\colortbl\red0\green0\blue0;}\cf1 Black text\par}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        var run = Assert.IsType<RunInline>(paragraph.Inlines[0]);
        Assert.NotNull(run.Style);
        Assert.True(run.Style!.Color.HasValue);
        Assert.Equal(new ProEdit.Primitives.DocColor(0, 0, 0), run.Style.Color!.Value);
    }

    [Fact]
    public void ParseRtf_HyperlinkField_AssignsRunHyperlink()
    {
        const string rtf = @"{\rtf1\ansi{\field{\*\fldinst HYPERLINK ""https://example.com"" \\o ""Example tooltip""}{\fldrslt Click here}}\par}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        var run = Assert.IsType<RunInline>(paragraph.Inlines[0]);
        Assert.Equal("Click here", run.GetText());
        Assert.NotNull(run.Hyperlink);
        Assert.Equal("https://example.com", run.Hyperlink!.Uri);
        Assert.Equal("Example tooltip", run.Hyperlink!.Tooltip);
    }

    [Fact]
    public void SerializeAndParseRtf_HyperlinkRun_RoundTrips()
    {
        var document = new Document();
        var paragraph = new ParagraphBlock();
        var run = new RunInline("Visit")
        {
            Hyperlink = new HyperlinkInfo("https://example.com/docs", "section1", "Go to docs")
        };
        paragraph.Inlines.Add(run);
        document.Blocks.Add(paragraph);

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"\fldinst HYPERLINK", rtf);
        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));

        var parsedRun = parsed.Blocks
            .OfType<ParagraphBlock>()
            .SelectMany(paragraphBlock => paragraphBlock.Inlines.OfType<RunInline>())
            .FirstOrDefault(candidate => candidate.Hyperlink is not null);
        Assert.NotNull(parsedRun);
        Assert.Equal("Visit", parsedRun.GetText());
        Assert.NotNull(parsedRun.Hyperlink);
        Assert.Equal("https://example.com/docs", parsedRun.Hyperlink!.Uri);
        Assert.Equal("section1", parsedRun.Hyperlink!.Anchor);
        Assert.Equal("Go to docs", parsedRun.Hyperlink!.Tooltip);
    }

    [Fact]
    public void ParseRtf_PictureGroup_CreatesImageInline()
    {
        var png = GetTinyPngBytes();
        var hex = Convert.ToHexString(png);
        var rtf = $@"{{\rtf1\ansi{{\pict\pngblip\picw1\pich1\picwgoal20\pichgoal20 {hex}}}\par}}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        var image = Assert.IsType<ImageInline>(paragraph.Inlines[0]);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(png, image.Data);
        Assert.True(image.Width > 0f);
        Assert.True(image.Height > 0f);
    }

    [Fact]
    public void SerializeAndParseRtf_ImageInline_RoundTrips()
    {
        var png = GetTinyPngBytes();
        var document = new Document();
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new ImageInline(png, 18f, 12f, "image/png"));
        document.Blocks.Add(paragraph);

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"\pict", rtf);
        Assert.Contains(@"\pngblip", rtf);
        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));

        var parsedImage = parsed.Blocks
            .OfType<ParagraphBlock>()
            .SelectMany(paragraphBlock => paragraphBlock.Inlines)
            .OfType<ImageInline>()
            .FirstOrDefault();
        Assert.NotNull(parsedImage);
        Assert.Equal("image/png", parsedImage.ContentType);
        Assert.Equal(png, parsedImage.Data);
        Assert.InRange(parsedImage.Width, 17f, 19f);
        Assert.InRange(parsedImage.Height, 11f, 13f);
    }

    [Fact]
    public void ParseRtf_StarShpPict_PictureIsParsed()
    {
        var png = GetTinyPngBytes();
        var hex = Convert.ToHexString(png);
        var rtf = $@"{{\rtf1\ansi{{\*\shppict{{\pict\pngblip\picw1\pich1\picwgoal20\pichgoal20 {hex}}}}}\par}}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        var parsedImage = document.Blocks
            .OfType<ParagraphBlock>()
            .SelectMany(paragraphBlock => paragraphBlock.Inlines)
            .OfType<ImageInline>()
            .FirstOrDefault();
        Assert.NotNull(parsedImage);
        Assert.Equal("image/png", parsedImage.ContentType);
        Assert.Equal(png, parsedImage.Data);
    }

    [Fact]
    public void ParseRtf_TableCellFormatting_AppliesRowAndCellProperties()
    {
        const string rtf = @"{\rtf1\ansi{\colortbl;\red238\green243\blue255;\red51\green68\blue85;}\trowd\trgaph120\trpaddl80\trpaddfl3\trpaddr100\trpaddfr3\trpaddt60\trpaddft3\trpaddb40\trpaddfb3\trrh-240\trhdr\clcbpat1\clvertalc\clpadl40\clpadfl3\clpadt20\clpadft3\clpadr30\clpadfr3\clpadb10\clpadfb3\clbrdrt\brdrs\brdrw20\brdrcf2\clbrdrb\brdrdb\brdrw40\brdrcf2\clwWidth1440\clftsWidth3\cellx2000\intbl Cell\cell\row}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        var table = Assert.IsType<TableBlock>(Assert.Single(document.Blocks));
        Assert.True(table.Properties.CellSpacing.HasValue);
        Assert.Equal(TableWidthUnit.Dxa, table.Properties.CellSpacingUnit);
        Assert.InRange(table.Properties.CellSpacing!.Value, TwipsToDip(119), TwipsToDip(121));
        Assert.True(table.Properties.CellPadding.HasValue);
        var tablePadding = table.Properties.CellPadding!.Value;
        Assert.InRange(tablePadding.Left, TwipsToDip(79), TwipsToDip(81));
        Assert.InRange(tablePadding.Top, TwipsToDip(59), TwipsToDip(61));
        Assert.InRange(tablePadding.Right, TwipsToDip(99), TwipsToDip(101));
        Assert.InRange(tablePadding.Bottom, TwipsToDip(39), TwipsToDip(41));

        var row = Assert.Single(table.Rows);
        Assert.Equal(TableRowHeightRule.Exact, row.Properties.HeightRule);
        Assert.True(row.Properties.Height.HasValue);
        Assert.InRange(row.Properties.Height!.Value, TwipsToDip(239), TwipsToDip(241));
        Assert.True(row.Properties.RepeatOnEachPage);

        var cell = Assert.Single(row.Cells);
        Assert.Equal(TableCellVerticalAlignment.Center, cell.Properties.VerticalAlignment);
        Assert.Equal(new DocColor(238, 243, 255), cell.Properties.ShadingColor);
        Assert.True(cell.Properties.Padding.HasValue);
        var cellPadding = cell.Properties.Padding!.Value;
        Assert.InRange(cellPadding.Left, TwipsToDip(39), TwipsToDip(41));
        Assert.InRange(cellPadding.Top, TwipsToDip(19), TwipsToDip(21));
        Assert.InRange(cellPadding.Right, TwipsToDip(29), TwipsToDip(31));
        Assert.InRange(cellPadding.Bottom, TwipsToDip(9), TwipsToDip(11));
        Assert.NotNull(cell.Properties.Borders.Top);
        Assert.Equal(DocBorderStyle.Single, cell.Properties.Borders.Top!.Style);
        Assert.Equal(new DocColor(51, 68, 85), cell.Properties.Borders.Top.Color);
        Assert.NotNull(cell.Properties.Borders.Bottom);
        Assert.Equal(DocBorderStyle.Double, cell.Properties.Borders.Bottom!.Style);
        Assert.Equal(new DocColor(51, 68, 85), cell.Properties.Borders.Bottom.Color);
        Assert.Equal(TableWidthUnit.Dxa, cell.Properties.PreferredWidthUnit);
        Assert.True(cell.Properties.PreferredWidth.HasValue);
        Assert.InRange(cell.Properties.PreferredWidth!.Value, TwipsToDip(1439), TwipsToDip(1441));
    }

    [Fact]
    public void SerializeAndParseRtf_TableCellFormatting_RoundTrips()
    {
        var document = new Document();
        var table = new TableBlock();
        table.Properties.CellSpacing = 8f;
        table.Properties.CellSpacingUnit = TableWidthUnit.Dxa;
        table.Properties.CellPadding = new DocThickness(3f, 4f, 5f, 6f);

        var row = new DocumentTableRow();
        row.Properties.Height = 24f;
        row.Properties.HeightRule = TableRowHeightRule.Exact;
        row.Properties.RepeatOnEachPage = true;

        var cell = new DocumentTableCell
        {
            VerticalMerge = TableCellVerticalMerge.Restart
        };
        cell.Properties.ShadingColor = new DocColor(238, 243, 255);
        cell.Properties.VerticalAlignment = TableCellVerticalAlignment.Bottom;
        cell.Properties.Padding = new DocThickness(4f, 2f, 6f, 3f);
        cell.Properties.PreferredWidth = 72f;
        cell.Properties.PreferredWidthUnit = TableWidthUnit.Dxa;
        cell.Properties.Borders.Top = new BorderLine
        {
            Style = DocBorderStyle.Single,
            Thickness = 1.5f,
            Color = new DocColor(51, 68, 85)
        };
        cell.Properties.Borders.Bottom = new BorderLine
        {
            Style = DocBorderStyle.Dashed,
            Thickness = 2f,
            Color = new DocColor(51, 68, 85)
        };
        cell.Blocks.Add(new ParagraphBlock("Cell"));
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"\trrh-", rtf);
        Assert.Contains(@"\trhdr", rtf);
        Assert.Contains(@"\trgaph", rtf);
        Assert.Contains(@"\trpaddl", rtf);
        Assert.Contains(@"\clvmgf", rtf);
        Assert.Contains(@"\clcbpat", rtf);
        Assert.Contains(@"\clvertalb", rtf);
        Assert.Contains(@"\clpadl", rtf);
        Assert.Contains(@"\clbrdrt", rtf);
        Assert.Contains(@"\brdrdash", rtf);

        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));
        var parsedTable = Assert.Single(parsed.Blocks.OfType<TableBlock>());
        var parsedRow = Assert.Single(parsedTable.Rows);
        Assert.Equal(TableRowHeightRule.Exact, parsedRow.Properties.HeightRule);
        Assert.True(parsedRow.Properties.RepeatOnEachPage);

        var parsedCell = Assert.Single(parsedRow.Cells);
        Assert.Equal(TableCellVerticalMerge.Restart, parsedCell.VerticalMerge);
        Assert.Equal(TableCellVerticalAlignment.Bottom, parsedCell.Properties.VerticalAlignment);
        Assert.Equal(new DocColor(238, 243, 255), parsedCell.Properties.ShadingColor);
        Assert.True(parsedCell.Properties.Padding.HasValue);
        Assert.NotNull(parsedCell.Properties.Borders.Top);
        Assert.Equal(DocBorderStyle.Single, parsedCell.Properties.Borders.Top!.Style);
        Assert.NotNull(parsedCell.Properties.Borders.Bottom);
        Assert.Equal(DocBorderStyle.Dashed, parsedCell.Properties.Borders.Bottom!.Style);
        Assert.Equal(TableWidthUnit.Dxa, parsedCell.Properties.PreferredWidthUnit);
        Assert.True(parsedCell.Properties.PreferredWidth.HasValue);
    }

    [Fact]
    public void ParseRtf_TableProperties_AppliesAlignmentWidthLayoutAndBorders()
    {
        const string rtf = @"{\rtf1\ansi{\colortbl;\red34\green85\blue136;}\trowd\trqc\trautofit0\trftsWidth3\trwWidth4320\tblind240\trbrdrt\brdrs\brdrw20\brdrcf1\trbrdrh\brdrdot\brdrw15\brdrcf1\trbrdrv\brdrdash\brdrw10\brdrcf1\cellx2000\intbl A\cell\row}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        var table = Assert.IsType<TableBlock>(Assert.Single(document.Blocks));
        Assert.Equal(TableAlignment.Center, table.Properties.Alignment);
        Assert.Equal(TableLayoutMode.Fixed, table.Properties.LayoutMode);
        Assert.Equal(TableWidthUnit.Dxa, table.Properties.WidthUnit);
        Assert.True(table.Properties.Width.HasValue);
        Assert.InRange(table.Properties.Width!.Value, TwipsToDip(4319), TwipsToDip(4321));
        Assert.Equal(TableWidthUnit.Dxa, table.Properties.IndentUnit);
        Assert.True(table.Properties.Indent.HasValue);
        Assert.InRange(table.Properties.Indent!.Value, TwipsToDip(239), TwipsToDip(241));

        Assert.NotNull(table.Properties.Borders.Top);
        Assert.Equal(DocBorderStyle.Single, table.Properties.Borders.Top!.Style);
        Assert.Equal(new DocColor(34, 85, 136), table.Properties.Borders.Top.Color);
        Assert.NotNull(table.Properties.Borders.InsideHorizontal);
        Assert.Equal(DocBorderStyle.Dotted, table.Properties.Borders.InsideHorizontal!.Style);
        Assert.NotNull(table.Properties.Borders.InsideVertical);
        Assert.Equal(DocBorderStyle.Dashed, table.Properties.Borders.InsideVertical!.Style);
    }

    [Fact]
    public void SerializeAndParseRtf_TableProperties_RoundTrips()
    {
        var document = new Document();
        var table = new TableBlock();
        table.Properties.Alignment = TableAlignment.Right;
        table.Properties.LayoutMode = TableLayoutMode.Auto;
        table.Properties.Width = 50f;
        table.Properties.WidthUnit = TableWidthUnit.Pct;
        table.Properties.Indent = 18f;
        table.Properties.IndentUnit = TableWidthUnit.Dxa;
        table.Properties.Borders.Top = new BorderLine
        {
            Style = DocBorderStyle.Single,
            Thickness = 1.25f,
            Color = new DocColor(34, 85, 136)
        };
        table.Properties.Borders.InsideHorizontal = new BorderLine
        {
            Style = DocBorderStyle.Dotted,
            Thickness = 0.75f,
            Color = new DocColor(34, 85, 136)
        };
        table.Properties.Borders.InsideVertical = new BorderLine
        {
            Style = DocBorderStyle.Double,
            Thickness = 1f,
            Color = new DocColor(34, 85, 136)
        };

        var row = new DocumentTableRow();
        var cell = new DocumentTableCell();
        cell.Blocks.Add(new ParagraphBlock("Cell"));
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"\trqr", rtf);
        Assert.Contains(@"\trautofit1", rtf);
        Assert.Contains(@"\trftsWidth2\trwWidth2500", rtf);
        Assert.Contains(@"\trbrdrt", rtf);
        Assert.Contains(@"\trbrdrh", rtf);
        Assert.Contains(@"\trbrdrv", rtf);

        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));
        var parsedTable = Assert.Single(parsed.Blocks.OfType<TableBlock>());
        Assert.Equal(TableAlignment.Right, parsedTable.Properties.Alignment);
        Assert.Equal(TableLayoutMode.Auto, parsedTable.Properties.LayoutMode);
        Assert.Equal(TableWidthUnit.Pct, parsedTable.Properties.WidthUnit);
        Assert.True(parsedTable.Properties.Width.HasValue);
        Assert.InRange(parsedTable.Properties.Width!.Value, 49.9f, 50.1f);
        Assert.True(parsedTable.Properties.Indent.HasValue);
        Assert.NotNull(parsedTable.Properties.Borders.Top);
        Assert.Equal(DocBorderStyle.Single, parsedTable.Properties.Borders.Top!.Style);
        Assert.NotNull(parsedTable.Properties.Borders.InsideHorizontal);
        Assert.Equal(DocBorderStyle.Dotted, parsedTable.Properties.Borders.InsideHorizontal!.Style);
        Assert.NotNull(parsedTable.Properties.Borders.InsideVertical);
        Assert.Equal(DocBorderStyle.Double, parsedTable.Properties.Borders.InsideVertical!.Style);
    }

    [Fact]
    public void ParseRtf_NativeListTable_AssignsListInfoAndDefinition()
    {
        const string rtf = @"{\rtf1\ansi{\listtable{\list\listtemplateid1{\listlevel\levelnfc23\levelnfcn23\levelstartat1{\leveltext \u8226 ?;}{\levelnumbers;}\fi-360\li720\tx720}\listid5}}{\listoverridetable{\listoverride\listid5\listoverridecount0\ls3}}\pard\ls3\ilvl0{\listtext\u8226 ?\tab}Item one\par}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        Assert.True(document.ListDefinitions.ContainsKey(5));
        var definition = document.ListDefinitions[5];
        Assert.True(definition.Levels.ContainsKey(0));
        Assert.Equal(ListNumberFormat.Bullet, definition.Levels[0].Format);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        Assert.NotNull(paragraph.ListInfo);
        Assert.Equal(ListKind.Bullet, paragraph.ListInfo!.Kind);
        Assert.Equal(5, paragraph.ListInfo.ListId);
        Assert.Equal(0, paragraph.ListInfo.Level);
        Assert.Equal("•", paragraph.ListInfo.BulletSymbol);
        Assert.Equal("Item one", ExtractParagraphText(paragraph));
    }

    [Fact]
    public void ParseRtf_NativeNumberedList_DoesNotLeakListTextMarker()
    {
        const string rtf = @"{\rtf1\ansi{\listtable{\list\listtemplateid2{\listlevel\levelnfc2\levelnfcn2\levelstartat1{\leveltext %1.;}{\levelnumbers;}\fi-360\li720\tx720}\listid6}}{\listoverridetable{\listoverride\listid6\listoverridecount0\ls4}}\pard\ls4\ilvl0{\listtext iv.\tab}Numbered item\par}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        var listInfo = Assert.IsType<ListInfo>(paragraph.ListInfo);
        Assert.Equal(ListKind.Numbered, listInfo.Kind);
        Assert.Equal(ListNumberFormat.LowerRoman, listInfo.NumberFormat);
        Assert.Equal(6, listInfo.ListId);
        Assert.Equal("Numbered item", ExtractParagraphText(paragraph));
    }

    [Fact]
    public void SerializeAndParseRtf_NativeListMetadata_RoundTrips()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.ListDefinitions[12] = ListDefinitionDefaults.CreateBulleted(12, 1);

        var first = new ParagraphBlock
        {
            ListInfo = new ListInfo(ListKind.Bullet, level: 0, listId: 12)
            {
                NumberFormat = ListNumberFormat.Bullet,
                BulletSymbol = "•"
            }
        };
        first.Inlines.Add(new RunInline("First item"));

        var second = new ParagraphBlock
        {
            ListInfo = new ListInfo(ListKind.Bullet, level: 0, listId: 12)
            {
                NumberFormat = ListNumberFormat.Bullet,
                BulletSymbol = "•"
            }
        };
        second.Inlines.Add(new RunInline("Second item"));

        document.Blocks.Add(first);
        document.Blocks.Add(second);

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"\listtable", rtf);
        Assert.Contains(@"\listoverridetable", rtf);
        Assert.Contains(@"\ls12\ilvl0", rtf);

        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));
        var parsedParagraphs = parsed.Blocks.OfType<ParagraphBlock>().ToList();
        Assert.Equal(2, parsedParagraphs.Count);
        Assert.True(parsed.ListDefinitions.ContainsKey(12));

        var firstInfo = Assert.IsType<ListInfo>(parsedParagraphs[0].ListInfo);
        Assert.Equal(ListKind.Bullet, firstInfo.Kind);
        Assert.Equal(12, firstInfo.ListId);
        Assert.Equal("First item", ExtractParagraphText(parsedParagraphs[0]));

        var secondInfo = Assert.IsType<ListInfo>(parsedParagraphs[1].ListInfo);
        Assert.Equal(ListKind.Bullet, secondInfo.Kind);
        Assert.Equal(12, secondInfo.ListId);
        Assert.Equal("Second item", ExtractParagraphText(parsedParagraphs[1]));
    }

    [Fact]
    public void ParseRtf_SectionPageSetup_AppliesSectionPropertiesAndDocumentFlags()
    {
        const string rtf = @"{\rtf1\ansi\sectd\margmirror\facingp\gutterprl\paperw15840\paperh12240\landscape\margl1440\margr1080\margt720\margb900\headery360\footery420\gutter180\titlepg\cols3\colsx240\linebetcol\colno0\colw4000\colsr180\colno1\colw4200\colsr200\colno2\colw4400\pard Layout\par}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        Assert.True(document.MirrorMargins);
        Assert.True(document.EvenAndOddHeaders);
        Assert.True(document.GutterAtTop);

        var section = document.SectionProperties;
        Assert.Equal(PageOrientation.Landscape, section.Orientation);
        Assert.True(section.PageWidth.HasValue);
        Assert.InRange(section.PageWidth!.Value, TwipsToDip(15839), TwipsToDip(15841));
        Assert.True(section.PageHeight.HasValue);
        Assert.InRange(section.PageHeight!.Value, TwipsToDip(12239), TwipsToDip(12241));
        Assert.True(section.MarginLeft.HasValue);
        Assert.InRange(section.MarginLeft!.Value, TwipsToDip(1439), TwipsToDip(1441));
        Assert.True(section.MarginRight.HasValue);
        Assert.InRange(section.MarginRight!.Value, TwipsToDip(1079), TwipsToDip(1081));
        Assert.True(section.MarginTop.HasValue);
        Assert.InRange(section.MarginTop!.Value, TwipsToDip(719), TwipsToDip(721));
        Assert.True(section.MarginBottom.HasValue);
        Assert.InRange(section.MarginBottom!.Value, TwipsToDip(899), TwipsToDip(901));
        Assert.True(section.HeaderOffset.HasValue);
        Assert.InRange(section.HeaderOffset!.Value, TwipsToDip(359), TwipsToDip(361));
        Assert.True(section.FooterOffset.HasValue);
        Assert.InRange(section.FooterOffset!.Value, TwipsToDip(419), TwipsToDip(421));
        Assert.True(section.Gutter.HasValue);
        Assert.InRange(section.Gutter!.Value, TwipsToDip(179), TwipsToDip(181));
        Assert.True(section.DifferentFirstPageHeaderFooter);
        Assert.Equal(3, section.ColumnCount);
        Assert.True(section.ColumnGap.HasValue);
        Assert.InRange(section.ColumnGap!.Value, TwipsToDip(239), TwipsToDip(241));
        Assert.True(section.ColumnSeparator);
        Assert.False(section.ColumnEqualWidth);
        Assert.Equal(3, section.ColumnWidths.Count);
        Assert.InRange(section.ColumnWidths[0], TwipsToDip(3999), TwipsToDip(4001));
        Assert.InRange(section.ColumnWidths[1], TwipsToDip(4199), TwipsToDip(4201));
        Assert.InRange(section.ColumnWidths[2], TwipsToDip(4399), TwipsToDip(4401));
        Assert.Equal(2, section.ColumnGaps.Count);
        Assert.InRange(section.ColumnGaps[0], TwipsToDip(179), TwipsToDip(181));
        Assert.InRange(section.ColumnGaps[1], TwipsToDip(199), TwipsToDip(201));
    }

    [Fact]
    public void SerializeAndParseRtf_SectionPageSetup_RoundTrips()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.MirrorMargins = true;
        document.EvenAndOddHeaders = true;
        document.GutterAtTop = true;

        var section = document.SectionProperties;
        section.PageWidth = TwipsToDip(15840);
        section.PageHeight = TwipsToDip(12240);
        section.Orientation = PageOrientation.Landscape;
        section.MarginLeft = TwipsToDip(1440);
        section.MarginRight = TwipsToDip(1080);
        section.MarginTop = TwipsToDip(720);
        section.MarginBottom = TwipsToDip(900);
        section.HeaderOffset = TwipsToDip(360);
        section.FooterOffset = TwipsToDip(420);
        section.Gutter = TwipsToDip(180);
        section.DifferentFirstPageHeaderFooter = true;
        section.ColumnCount = 3;
        section.ColumnGap = TwipsToDip(240);
        section.ColumnSeparator = true;
        section.ColumnEqualWidth = false;
        section.ColumnWidths.Add(TwipsToDip(4000));
        section.ColumnWidths.Add(TwipsToDip(4200));
        section.ColumnWidths.Add(TwipsToDip(4400));
        section.ColumnGaps.Add(TwipsToDip(180));
        section.ColumnGaps.Add(TwipsToDip(200));

        document.Blocks.Add(new ParagraphBlock("Layout"));

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"\sectd", rtf);
        Assert.Contains(@"\margmirror", rtf);
        Assert.Contains(@"\facingp", rtf);
        Assert.Contains(@"\gutterprl", rtf);
        Assert.Contains(@"\paperw15840", rtf);
        Assert.Contains(@"\paperh12240", rtf);
        Assert.Contains(@"\landscape", rtf);
        Assert.Contains(@"\margl1440", rtf);
        Assert.Contains(@"\margr1080", rtf);
        Assert.Contains(@"\margt720", rtf);
        Assert.Contains(@"\margb900", rtf);
        Assert.Contains(@"\headery360", rtf);
        Assert.Contains(@"\footery420", rtf);
        Assert.Contains(@"\gutter180", rtf);
        Assert.Contains(@"\titlepg", rtf);
        Assert.Contains(@"\cols3", rtf);
        Assert.Contains(@"\colsx240", rtf);
        Assert.Contains(@"\linebetcol", rtf);
        Assert.Contains(@"\colno0\colw4000\colsr180", rtf);
        Assert.Contains(@"\colno1\colw4200\colsr200", rtf);
        Assert.Contains(@"\colno2\colw4400", rtf);

        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));
        Assert.True(parsed.MirrorMargins);
        Assert.True(parsed.EvenAndOddHeaders);
        Assert.True(parsed.GutterAtTop);
        Assert.Equal(PageOrientation.Landscape, parsed.SectionProperties.Orientation);
        Assert.Equal(3, parsed.SectionProperties.ColumnCount);
        Assert.True(parsed.SectionProperties.DifferentFirstPageHeaderFooter);
        Assert.True(parsed.SectionProperties.ColumnSeparator);
        Assert.False(parsed.SectionProperties.ColumnEqualWidth);
        Assert.Equal(3, parsed.SectionProperties.ColumnWidths.Count);
        Assert.Equal(2, parsed.SectionProperties.ColumnGaps.Count);
    }

    [Fact]
    public void ParseRtf_BreakControls_CreateBreakBlocks()
    {
        const string rtf = @"{\rtf1\ansi First\par\page\column\sbkeven\sect Second\par}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        Assert.Collection(
            document.Blocks,
            block => Assert.Equal("First", ExtractParagraphText(Assert.IsType<ParagraphBlock>(block))),
            block => Assert.IsType<PageBreakBlock>(block),
            block => Assert.IsType<ColumnBreakBlock>(block),
            block =>
            {
                var sectionBreak = Assert.IsType<SectionBreakBlock>(block);
                Assert.Equal(SectionBreakType.EvenPage, sectionBreak.BreakType);
            },
            block => Assert.Equal("Second", ExtractParagraphText(Assert.IsType<ParagraphBlock>(block))));
    }

    [Fact]
    public void SerializeAndParseRtf_BreakBlocks_RoundTrip()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Before"));
        document.Blocks.Add(new PageBreakBlock());
        document.Blocks.Add(new ColumnBreakBlock());
        document.Blocks.Add(new SectionBreakBlock
        {
            BreakType = SectionBreakType.OddPage,
            Properties = new SectionProperties
            {
                Orientation = PageOrientation.Landscape,
                MarginLeft = TwipsToDip(1440)
            }
        });
        document.Blocks.Add(new ParagraphBlock("After"));

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"\page", rtf);
        Assert.Contains(@"\column", rtf);
        Assert.Contains(@"\sbkodd\sect", rtf);
        Assert.Contains(@"\sectd", rtf);
        Assert.Contains(@"\landscape", rtf);

        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));
        Assert.Collection(
            parsed.Blocks,
            block => Assert.Equal("Before", ExtractParagraphText(Assert.IsType<ParagraphBlock>(block))),
            block => Assert.IsType<PageBreakBlock>(block),
            block => Assert.IsType<ColumnBreakBlock>(block),
            block =>
            {
                var sectionBreak = Assert.IsType<SectionBreakBlock>(block);
                Assert.Equal(SectionBreakType.OddPage, sectionBreak.BreakType);
                Assert.Equal(PageOrientation.Landscape, sectionBreak.Properties.Orientation);
                Assert.True(sectionBreak.Properties.MarginLeft.HasValue);
            },
            block => Assert.Equal("After", ExtractParagraphText(Assert.IsType<ParagraphBlock>(block))));
    }

    [Fact]
    public void ParseRtf_HeaderFooterDestinations_AssignsDocumentHeaderFooters()
    {
        const string rtf = @"{\rtf1\ansi\sectd\facingp\titlepg{\header\pard Default Header\par}{\footer\pard Default Footer\par}{\headerf\pard First Header\par}{\footerf\pard First Footer\par}{\headerl\pard Even Header\par}{\footerl\pard Even Footer\par}\pard Body\par}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        Assert.True(document.EvenAndOddHeaders);
        Assert.True(document.SectionProperties.DifferentFirstPageHeaderFooter);
        Assert.True(document.Header.IsDefined);
        Assert.True(document.Footer.IsDefined);
        Assert.True(document.FirstHeader.IsDefined);
        Assert.True(document.FirstFooter.IsDefined);
        Assert.True(document.EvenHeader.IsDefined);
        Assert.True(document.EvenFooter.IsDefined);
        Assert.Equal("Default Header", ExtractHeaderFooterText(document.Header));
        Assert.Equal("Default Footer", ExtractHeaderFooterText(document.Footer));
        Assert.Equal("First Header", ExtractHeaderFooterText(document.FirstHeader));
        Assert.Equal("First Footer", ExtractHeaderFooterText(document.FirstFooter));
        Assert.Equal("Even Header", ExtractHeaderFooterText(document.EvenHeader));
        Assert.Equal("Even Footer", ExtractHeaderFooterText(document.EvenFooter));
        Assert.Equal("Body", ExtractParagraphText(Assert.IsType<ParagraphBlock>(document.Blocks[0])));
    }

    [Fact]
    public void SerializeAndParseRtf_HeaderFooterDestinations_RoundTrip()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Body"));
        document.EvenAndOddHeaders = true;
        document.SectionProperties.DifferentFirstPageHeaderFooter = true;

        var styledHeader = new ParagraphBlock();
        styledHeader.Inlines.Add(new RunInline("Default Header")
        {
            Style = new TextStyleProperties
            {
                FontFamily = "Consolas",
                Color = new DocColor(7, 8, 9)
            }
        });

        document.Header.IsDefined = true;
        document.Header.Blocks.Add(styledHeader);
        document.Footer.IsDefined = true;
        document.Footer.Blocks.Add(new ParagraphBlock("Default Footer"));
        document.FirstHeader.IsDefined = true;
        document.FirstHeader.Blocks.Add(new ParagraphBlock("First Header"));
        document.FirstFooter.IsDefined = true;
        document.FirstFooter.Blocks.Add(new ParagraphBlock("First Footer"));
        document.EvenHeader.IsDefined = true;
        document.EvenHeader.Blocks.Add(new ParagraphBlock("Even Header"));
        document.EvenFooter.IsDefined = true;
        document.EvenFooter.Blocks.Add(new ParagraphBlock("Even Footer"));

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"{\header", rtf);
        Assert.Contains(@"{\footer", rtf);
        Assert.Contains(@"{\headerf", rtf);
        Assert.Contains(@"{\footerf", rtf);
        Assert.Contains(@"{\headerl", rtf);
        Assert.Contains(@"{\footerl", rtf);
        Assert.Contains("Consolas", rtf);
        Assert.Contains(@"\red7\green8\blue9;", rtf);

        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));
        Assert.True(parsed.EvenAndOddHeaders);
        Assert.True(parsed.SectionProperties.DifferentFirstPageHeaderFooter);
        Assert.Equal("Default Header", ExtractHeaderFooterText(parsed.Header));
        Assert.Equal("Default Footer", ExtractHeaderFooterText(parsed.Footer));
        Assert.Equal("First Header", ExtractHeaderFooterText(parsed.FirstHeader));
        Assert.Equal("First Footer", ExtractHeaderFooterText(parsed.FirstFooter));
        Assert.Equal("Even Header", ExtractHeaderFooterText(parsed.EvenHeader));
        Assert.Equal("Even Footer", ExtractHeaderFooterText(parsed.EvenFooter));
        Assert.Equal("Body", ExtractParagraphText(Assert.IsType<ParagraphBlock>(parsed.Blocks[0])));
    }

    [Fact]
    public void SerializeRtf_FirstHeaderDestination_EnablesTitlePageFlag()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Body"));
        document.FirstHeader.IsDefined = true;
        document.FirstHeader.Blocks.Add(new ParagraphBlock("First Header"));

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"\titlepg", rtf);
        Assert.DoesNotContain(@"\titlepg0", rtf);

        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));
        Assert.True(parsed.SectionProperties.DifferentFirstPageHeaderFooter);
        Assert.Equal("First Header", ExtractHeaderFooterText(parsed.FirstHeader));
    }

    [Fact]
    public void SerializeRtf_EvenHeaderDestination_EnablesFacingPagesFlag()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Body"));
        document.EvenHeader.IsDefined = true;
        document.EvenHeader.Blocks.Add(new ParagraphBlock("Even Header"));

        var rtf = DocumentRtfSerializer.ToRtf(document);
        Assert.Contains(@"\facingp", rtf);

        Assert.True(DocumentRtfParser.TryParse(rtf, out var parsed));
        Assert.True(parsed.EvenAndOddHeaders);
        Assert.Equal("Even Header", ExtractHeaderFooterText(parsed.EvenHeader));
    }

    [Fact]
    public void ParseRtf_HeaderDestination_WithEscapedOpenBrace_ParsesCorrectGroupBoundary()
    {
        const string rtf = @"{\rtf1\ansi{\header\pard Open \{ brace\par}\pard Body\par}";
        Assert.True(DocumentRtfParser.TryParse(rtf, out var document));

        Assert.True(document.Header.IsDefined);
        Assert.Equal("Open { brace", ExtractHeaderFooterText(document.Header));
        Assert.Equal("Body", ExtractParagraphText(Assert.IsType<ParagraphBlock>(document.Blocks[0])));
    }

    private static string ExtractParagraphText(ParagraphBlock paragraph)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < paragraph.Inlines.Count; i++)
        {
            if (paragraph.Inlines[i] is RunInline run)
            {
                builder.Append(run.GetText());
            }
        }

        return builder.ToString().Trim();
    }

    private static string ExtractHeaderFooterText(HeaderFooter headerFooter)
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(headerFooter.Blocks));
        return ExtractParagraphText(paragraph);
    }

    private static string ExtractText(ProEdit.FlowDocument.FlowDocument document)
    {
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            AppendBlockText(block, builder);
        }

        return builder.ToString();
    }

    private static void AppendBlockText(ProEdit.FlowDocument.Block block, StringBuilder builder)
    {
        switch (block)
        {
            case ProEdit.FlowDocument.Paragraph paragraph:
                foreach (var inline in paragraph.Inlines)
                {
                    AppendInlineText(inline, builder);
                }

                break;
            case ProEdit.FlowDocument.Section section:
                foreach (var child in section.Blocks)
                {
                    AppendBlockText(child, builder);
                }

                break;
            case ProEdit.FlowDocument.List list:
                foreach (var item in list.ListItems)
                {
                    foreach (var child in item.Blocks)
                    {
                        AppendBlockText(child, builder);
                    }
                }

                break;
            case ProEdit.FlowDocument.Table table:
                foreach (var group in table.RowGroups)
                {
                    foreach (var row in group.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var child in cell.Blocks)
                            {
                                AppendBlockText(child, builder);
                            }
                        }
                    }
                }

                break;
        }
    }

    private static void AppendInlineText(ProEdit.FlowDocument.Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case ProEdit.FlowDocument.Run run:
                builder.Append(run.Text);
                break;
            case ProEdit.FlowDocument.Span span:
                foreach (var child in span.Inlines)
                {
                    AppendInlineText(child, builder);
                }

                break;
            case ProEdit.FlowDocument.LineBreak:
                builder.AppendLine();
                break;
        }
    }

    private static byte[] GetTinyPngBytes()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+Y4QAAAAASUVORK5CYII=");
    }

    private static float TwipsToDip(int twips)
    {
        return twips / 20f * 96f / 72f;
    }

    private sealed class TempDirectoryFixture : IDisposable
    {
        public TempDirectoryFixture()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // best-effort cleanup for temporary test directory.
            }
        }
    }
}

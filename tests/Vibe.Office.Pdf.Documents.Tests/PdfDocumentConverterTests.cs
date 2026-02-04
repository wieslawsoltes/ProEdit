using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vibe.Office.Documents;
using Vibe.Office.Pdf;
using Vibe.Office.Pdf.Documents;
using Xunit;

namespace Vibe.Office.Pdf.Documents.Tests;

public sealed class PdfDocumentConverterTests
{
    [Fact]
    public void ReflowDetectsHeadingLevel3()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        page.TextRuns.Add(new PdfTextRun(
            "Heading",
            new PdfRect(72, 700, 120, 16),
            new PdfFontInfo("Test", false, false),
            12,
            708,
            0,
            null));

        page.TextRuns.Add(new PdfTextRun(
            "Body text",
            new PdfRect(72, 660, 160, 12),
            new PdfFontInfo("Test", false, false),
            10,
            668,
            0,
            null));

        page.TextRuns.Add(new PdfTextRun(
            "Body text 2",
            new PdfRect(72, 640, 180, 12),
            new PdfFontInfo("Test", false, false),
            10,
            648,
            0,
            null));

        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });

        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks.First());
        Assert.Equal("Heading3", paragraph.StyleId);
    }

    [Fact]
    public void ReflowDetectsNestedNumberedLists()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        page.TextRuns.Add(new PdfTextRun(
            "1. Item",
            new PdfRect(72, 700, 120, 12),
            new PdfFontInfo("Test", false, false),
            10,
            708,
            0,
            null));

        page.TextRuns.Add(new PdfTextRun(
            "1.1 Sub item",
            new PdfRect(96, 660, 160, 12),
            new PdfFontInfo("Test", false, false),
            10,
            668,
            0,
            null));

        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });
        var paragraphs = document.Blocks.OfType<ParagraphBlock>().ToList();

        Assert.True(paragraphs.Count >= 2);
        Assert.NotNull(paragraphs[0].ListInfo);
        Assert.NotNull(paragraphs[1].ListInfo);
        Assert.Equal(ListKind.Numbered, paragraphs[0].ListInfo?.Kind);
        Assert.Equal(0, paragraphs[0].ListInfo?.Level);
        Assert.Equal(1, paragraphs[1].ListInfo?.Level);

        var firstText = ExtractParagraphText(paragraphs[0]);
        var secondText = ExtractParagraphText(paragraphs[1]);
        Assert.Equal("Item", firstText);
        Assert.Equal("Sub item", secondText);
    }

    [Fact]
    public void FixedLayoutUsesNonBreakingSpacesAndAutoFit()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        page.TextRuns.Add(new PdfTextRun(
            "Hello World",
            new PdfRect(72, 700, 160, 12),
            new PdfFontInfo("Test", false, false),
            10,
            708,
            0,
            null));

        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.FixedLayout });
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks.First());
        var floating = Assert.Single(paragraph.FloatingObjects);
        var shape = Assert.IsType<ShapeInline>(floating.Content);
        Assert.NotNull(shape.TextBox);
        var textBox = shape.TextBox!;

        Assert.Equal(ShapeTextAutoFit.TextToFitShape, textBox.Properties.AutoFit);

        var lineParagraph = Assert.IsType<ParagraphBlock>(textBox.Blocks.First());
        var text = ExtractParagraphText(lineParagraph);
        Assert.Equal("Hello\u00A0World", text);
    }

    [Fact]
    public void FixedLayoutSplitsGlyphLinesAtLargeGaps()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        var glyphs = new List<PdfTextGlyph>();
        AddGlyphLine(glyphs, "Left", startX: 72, baselineY: 700, gap: 1);
        AddGlyphLine(glyphs, "Right", startX: 300, baselineY: 700, gap: 1);
        page.Glyphs.AddRange(glyphs);
        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.FixedLayout });
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks.First());

        var textShapes = paragraph.FloatingObjects
            .Select(obj => obj.Content)
            .OfType<ShapeInline>()
            .Where(shape => shape.TextBox is not null)
            .ToList();

        Assert.Equal(2, textShapes.Count);

        var texts = textShapes
            .Select(shape => ExtractParagraphText(Assert.IsType<ParagraphBlock>(shape.TextBox!.Blocks.First())))
            .OrderBy(text => text)
            .ToList();

        Assert.Equal(new[] { "Left", "Right" }, texts);
    }

    [Fact]
    public void FixedLayoutRegistersEmbeddedFontAliases()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        page.TextRuns.Add(new PdfTextRun(
            "Hello",
            new PdfRect(72, 700, 60, 12),
            new PdfFontInfo("SuzukiPROBold", true, false),
            10,
            708,
            0,
            null));

        pdf.Pages.Add(page);
        pdf.EmbeddedFonts.Add(new PdfEmbeddedFont(
            "SuzukiPRO Bold",
            new byte[] { 1, 2, 3 },
            "font/ttf",
            true,
            false,
            "SuzukiPROBold"));

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.FixedLayout });

        Assert.True(document.Fonts.FontTable.TryGetValue("SuzukiPROBold", out var aliasDefinition));
        Assert.NotNull(aliasDefinition.Bold);

        Assert.True(document.Fonts.FontTable.TryGetValue("SuzukiPRO Bold", out var familyDefinition));
        Assert.NotNull(familyDefinition.Bold);
    }

    [Fact]
    public void DiagnosticsTreatsPostScriptEmbeddedFontsAsAvailable()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        page.TextRuns.Add(new PdfTextRun(
            "Hello",
            new PdfRect(72, 700, 60, 12),
            new PdfFontInfo("SuzukiPROBold", true, false),
            10,
            708,
            0,
            null));

        pdf.Pages.Add(page);
        pdf.EmbeddedFonts.Add(new PdfEmbeddedFont(
            "SuzukiPRO Bold",
            new byte[] { 1, 2, 3 },
            "font/ttf",
            true,
            false,
            "SuzukiPROBold"));

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.FixedLayout });

        var hasDiagnostics = PdfImportDiagnosticsStore.TryRead(document, out _);
        Assert.False(hasDiagnostics);
    }

    [Fact]
    public void ReflowUsesGlyphSpacingForWordBoundaries()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        var glyphs = new List<PdfTextGlyph>();
        AddGlyphLine(glyphs, "Hello", startX: 72, baselineY: 700, gap: 1);
        AddGlyphLine(glyphs, "World", startX: 72 + 5 * 5 + 8, baselineY: 700, gap: 1);
        page.Glyphs.AddRange(glyphs);
        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });
        var paragraphs = document.Blocks.OfType<ParagraphBlock>().ToList();
        Console.WriteLine($"Paragraphs: {paragraphs.Count}");
        foreach (var para in paragraphs)
        {
            Console.WriteLine($"Paragraph: '{ExtractParagraphText(para)}'");
        }

        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks.First());
        var text = ExtractParagraphText(paragraph);

        Assert.Equal("Hello World", text);
    }

    [Fact]
    public void ReflowUsesBaselineGapWhenBoundsOverlap()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        var font = new PdfFontInfo("Test", false, false);
        var glyphs = new List<PdfTextGlyph>
        {
            new("A", new PdfRect(0, 690, 20, 10), font, 10, 0, 700, 5, PdfTextOrientation.Horizontal),
            new("B", new PdfRect(6, 690, 20, 10), font, 10, 6, 700, 5, PdfTextOrientation.Horizontal),
            new("C", new PdfRect(20, 690, 20, 10), font, 10, 20, 700, 5, PdfTextOrientation.Horizontal),
            new("D", new PdfRect(26, 690, 20, 10), font, 10, 26, 700, 5, PdfTextOrientation.Horizontal)
        };

        page.Glyphs.AddRange(glyphs);
        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks.First());
        var text = ExtractParagraphText(paragraph);

        Assert.Equal("AB CD", text);
    }

    [Fact]
    public void ReflowMergesHyphenatedLineBreaks()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        var glyphs = new List<PdfTextGlyph>();
        AddGlyphLine(glyphs, "hyphen-", startX: 72, baselineY: 700, gap: 1);
        AddGlyphLine(glyphs, "ation", startX: 72, baselineY: 688, gap: 1);
        page.Glyphs.AddRange(glyphs);
        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks.First());
        var text = ExtractParagraphText(paragraph);

        Assert.Equal("hyphenation", text);
    }

    [Fact]
    public void ReflowOrdersMultiColumnByXYCut()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        var glyphs = new List<PdfTextGlyph>();
        AddGlyphLine(glyphs, "Left1", startX: 72, baselineY: 700, gap: 1);
        AddGlyphLine(glyphs, "Right1", startX: 300, baselineY: 690, gap: 1);
        AddGlyphLine(glyphs, "Left2", startX: 72, baselineY: 660, gap: 1);
        AddGlyphLine(glyphs, "Right2", startX: 300, baselineY: 650, gap: 1);

        page.Glyphs.AddRange(glyphs);
        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });
        var paragraphs = document.Blocks.OfType<ParagraphBlock>().ToList();
        var texts = paragraphs.Select(ExtractParagraphText).ToList();

        Assert.Equal(new[] { "Left1", "Left2", "Right1", "Right2" }, texts);
    }

    [Fact]
    public void ReflowDetectsRuledTable()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst
        {
            Index = 0,
            Width = 612,
            Height = 792
        };

        page.Paths.Add(CreateLinePath(50, 700, 150, 700));
        page.Paths.Add(CreateLinePath(50, 650, 150, 650));
        page.Paths.Add(CreateLinePath(50, 600, 150, 600));
        page.Paths.Add(CreateLinePath(50, 600, 50, 700));
        page.Paths.Add(CreateLinePath(100, 600, 100, 700));
        page.Paths.Add(CreateLinePath(150, 600, 150, 700));

        var glyphs = new List<PdfTextGlyph>();
        AddGlyphLine(glyphs, "A1", startX: 60, baselineY: 685, gap: 1);
        AddGlyphLine(glyphs, "A2", startX: 110, baselineY: 685, gap: 1);
        AddGlyphLine(glyphs, "B1", startX: 60, baselineY: 635, gap: 1);
        AddGlyphLine(glyphs, "B2", startX: 110, baselineY: 635, gap: 1);
        page.Glyphs.AddRange(glyphs);

        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });
        var table = Assert.IsType<TableBlock>(document.Blocks.OfType<TableBlock>().First());

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);

        Assert.Equal("A1", ExtractCellText(table.Rows[0].Cells[0]));
        Assert.Equal("A2", ExtractCellText(table.Rows[0].Cells[1]));
        Assert.Equal("B1", ExtractCellText(table.Rows[1].Cells[0]));
        Assert.Equal("B2", ExtractCellText(table.Rows[1].Cells[1]));
    }

    [Fact]
    public void ReflowMapsHeadersAndFooters()
    {
        var pdf = new PdfDocumentAst();

        var page1 = new PdfPageAst { Index = 0, Width = 612, Height = 792 };
        var page2 = new PdfPageAst { Index = 1, Width = 612, Height = 792 };

        page1.Glyphs.AddRange(BuildTextLine("Header", 72, 760));
        page1.Glyphs.AddRange(BuildTextLine("Body1", 72, 500));
        page1.Glyphs.AddRange(BuildTextLine("Footer", 72, 20));

        page2.Glyphs.AddRange(BuildTextLine("Header", 72, 760));
        page2.Glyphs.AddRange(BuildTextLine("Body2", 72, 500));
        page2.Glyphs.AddRange(BuildTextLine("Footer", 72, 20));

        pdf.Pages.Add(page1);
        pdf.Pages.Add(page2);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });

        var headerText = ExtractParagraphText(document.Header.Blocks.OfType<ParagraphBlock>().First());
        var footerText = ExtractParagraphText(document.Footer.Blocks.OfType<ParagraphBlock>().First());
        var bodyParagraphs = document.Blocks.OfType<ParagraphBlock>().Select(ExtractParagraphText).ToList();

        Assert.Equal("Header", headerText);
        Assert.Equal("Footer", footerText);
        Assert.Equal(new[] { "Body1", "Body2" }, bodyParagraphs);
    }

    [Fact]
    public void ReflowDetectsFootnotes()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst { Index = 0, Width = 612, Height = 792 };

        page.Paths.Add(CreateLinePath(72, 100, 200, 100));

        var glyphs = new List<PdfTextGlyph>();
        AddGlyphLine(glyphs, "Body", startX: 72, baselineY: 500, gap: 1);
        AddGlyph(glyphs, "1", 72 + 4 * 6 + 2, 515, fontSize: 6);
        AddGlyphLine(glyphs, "1 Footnote", startX: 72, baselineY: 80, gap: 1);

        page.Glyphs.AddRange(glyphs);
        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });
        Assert.Single(document.Footnotes);

        var bodyParagraph = document.Blocks.OfType<ParagraphBlock>().First();
        var hasFootnoteRef = bodyParagraph.Inlines.OfType<FootnoteReferenceInline>().Any();
        Assert.True(hasFootnoteRef);

        var footnote = document.Footnotes.Values.First();
        var footnoteText = ExtractParagraphText(footnote.Blocks.OfType<ParagraphBlock>().First());
        Assert.Equal("Footnote", footnoteText);
    }

    [Fact]
    public void ReflowFallsBackToRunsWhenGlyphLinesAreFragmented()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst { Index = 0, Width = 612, Height = 792 };

        for (var i = 0; i < 12; i++)
        {
            var baselineY = 700 - i * 20;
            page.Glyphs.Add(new PdfTextGlyph(
                ((char)('A' + i)).ToString(),
                new PdfRect(72, baselineY - 10, 5, 10),
                new PdfFontInfo("Test", false, false),
                10,
                72,
                baselineY,
                5,
                PdfTextOrientation.Horizontal,
                null,
                0,
                i));
        }

        page.TextRuns.Add(new PdfTextRun(
            "Hello World",
            new PdfRect(72, 700, 160, 12),
            new PdfFontInfo("Test", false, false),
            10,
            708,
            0,
            null));

        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.Reflow });
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks.First());
        var text = ExtractParagraphText(paragraph);

        Assert.Equal("Hello World", text);
    }

    [Fact]
    public void FixedLayoutHandlesZeroHeightPaths()
    {
        var pdf = new PdfDocumentAst();
        var page = new PdfPageAst { Index = 0, Width = 612, Height = 792 };

        var path = new PdfPathObject
        {
            Bounds = new PdfRect(50, 100, 120, 0),
            Style = new PdfPathStyle
            {
                IsStroked = true,
                LineWidth = 0.5
            }
        };
        path.Segments.Add(PdfPathSegment.MoveTo(50, 100));
        path.Segments.Add(PdfPathSegment.LineTo(170, 100));
        page.Paths.Add(path);
        pdf.Pages.Add(page);

        var document = PdfDocumentConverter.FromPdf(pdf, new PdfImportOptions { Mode = PdfImportMode.FixedLayout });
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks.First());
        var floating = Assert.Single(paragraph.FloatingObjects);
        var shape = Assert.IsType<ShapeInline>(floating.Content);

        Assert.True(shape.Width > 0);
        Assert.True(shape.Height > 0);

        var geometry = shape.Properties.CustomGeometry;
        Assert.NotNull(geometry);
        var geometryPath = Assert.Single(geometry!.Paths);
        var move = Assert.IsType<ShapeMoveToCommand>(geometryPath.Commands.First());
        var x = double.Parse(move.Point.X, CultureInfo.InvariantCulture);
        var y = double.Parse(move.Point.Y, CultureInfo.InvariantCulture);
        Assert.InRange(x, 0, shape.Width);
        Assert.InRange(y, 0, shape.Height);
    }

    private static string ExtractParagraphText(ParagraphBlock paragraph)
    {
        return string.Concat(paragraph.Inlines.OfType<RunInline>().Select(run => run.GetText())).Trim();
    }

    private static void AddGlyphLine(List<PdfTextGlyph> target, string text, double startX, double baselineY, double gap)
    {
        var x = startX;
        var font = new PdfFontInfo("Test", false, false);
        const double width = 5;
        const double height = 10;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            var bounds = new PdfRect(x, baselineY - height, width, height);
            target.Add(new PdfTextGlyph(
                ch.ToString(),
                bounds,
                font,
                10,
                x,
                baselineY,
                width,
                PdfTextOrientation.Horizontal,
                null,
                0,
                target.Count));
            x += width + gap;
        }
    }

    private static PdfPathObject CreateLinePath(double x1, double y1, double x2, double y2)
    {
        var path = new PdfPathObject
        {
            Bounds = new PdfRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1))
        };
        path.Segments.Add(PdfPathSegment.MoveTo(x1, y1));
        path.Segments.Add(PdfPathSegment.LineTo(x2, y2));
        return path;
    }

    private static string ExtractCellText(TableCell cell)
    {
        var paragraphs = cell.Blocks.OfType<ParagraphBlock>();
        return string.Join(" ", paragraphs.Select(ExtractParagraphText)).Trim();
    }

    private static IEnumerable<PdfTextGlyph> BuildTextLine(string text, double startX, double baselineY)
    {
        var glyphs = new List<PdfTextGlyph>();
        AddGlyphLine(glyphs, text, startX, baselineY, 1);
        return glyphs;
    }

    private static void AddGlyph(List<PdfTextGlyph> target, string text, double x, double baselineY, double fontSize)
    {
        var font = new PdfFontInfo("Test", false, false);
        var width = fontSize * 0.6;
        var height = fontSize;
        var bounds = new PdfRect(x, baselineY - height, width, height);
        target.Add(new PdfTextGlyph(
            text,
            bounds,
            font,
            fontSize,
            x,
            baselineY,
            width,
            PdfTextOrientation.Horizontal,
            null,
            0,
            target.Count));
    }
}

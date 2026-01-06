using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Vibe.Office.OpenXml;
using Xunit;

namespace Vibe.Office.OpenXml.Tests;

public sealed class DocxRoundTripTests
{
    [Fact]
    public void RoundTrip_PreservesStylesAndDocDefaultsWithoutFlattening()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");
        var outputPath = Path.Combine(tempRoot, "output.docx");

        try
        {
            CreateInputDoc(inputPath);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            var exporter = new DocxExporter();
            exporter.Save(document, outputPath);

            using var outputDoc = WordprocessingDocument.Open(outputPath, false);
            var styles = outputDoc.MainDocumentPart?.StyleDefinitionsPart?.Styles;
            Assert.NotNull(styles);

            var docDefaults = styles!.DocDefaults;
            Assert.NotNull(docDefaults);
            var runDefaults = docDefaults!.RunPropertiesDefault?.RunPropertiesBaseStyle;
            Assert.NotNull(runDefaults);
            var runFonts = runDefaults!.RunFonts;
            Assert.NotNull(runFonts);
            Assert.Equal(ThemeFontValues.MinorAscii, runFonts!.AsciiTheme?.Value);
            Assert.Equal(ThemeFontValues.MinorHighAnsi, runFonts.HighAnsiTheme?.Value);

            var paragraphDefaults = docDefaults.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
            Assert.NotNull(paragraphDefaults);
            var spacing = paragraphDefaults!.SpacingBetweenLines;
            Assert.NotNull(spacing);
            Assert.Equal("360", spacing!.Line?.Value);
            Assert.Equal(LineSpacingRuleValues.Auto, spacing.LineRule?.Value);

            var derivedStyle = styles.Elements<Style>()
                .First(style => string.Equals(style.StyleId?.Value, "DerivedPara", StringComparison.Ordinal));
            Assert.Equal("BasePara", derivedStyle.BasedOn?.Val?.Value);

            var paragraph = outputDoc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
            Assert.Equal("DerivedPara", paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value);

            var run = paragraph.Elements<Run>().First();
            var runProps = run.RunProperties;
            Assert.NotNull(runProps);
            Assert.Equal("Emphasis", runProps!.RunStyle?.Val?.Value);
            Assert.Null(runProps.Bold);
            Assert.Null(runProps.Italic);
            Assert.NotNull(runProps.Underline);
            Assert.Equal(UnderlineValues.Dotted, runProps.Underline!.Val?.Value);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void RoundTrip_PreservesSdtNotesAndParagraphRunProperties()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");
        var outputPath = Path.Combine(tempRoot, "output.docx");

        try
        {
            CreateComplexInputDoc(inputPath);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            var exporter = new DocxExporter();
            exporter.Save(document, outputPath);

            using var outputDoc = WordprocessingDocument.Open(outputPath, false);
            var body = outputDoc.MainDocumentPart!.Document!.Body!;
            var paragraph = body.Elements<Paragraph>().First();

            var paragraphProperties = paragraph.ParagraphProperties;
            Assert.NotNull(paragraphProperties);
            Assert.NotNull(paragraphProperties!.GetFirstChild<BiDi>());
            var shading = paragraphProperties.GetFirstChild<Shading>();
            Assert.NotNull(shading);
            Assert.Equal("FFF2CC", shading!.Fill?.Value);
            var borders = paragraphProperties.GetFirstChild<ParagraphBorders>();
            Assert.NotNull(borders);
            Assert.Equal(BorderValues.Single, borders!.TopBorder?.Val?.Value);

            var runWithCaps = paragraph.Descendants<Run>()
                .First(run => run.RunProperties?.SmallCaps is not null);
            Assert.NotNull(runWithCaps.RunProperties!.SmallCaps);
            Assert.Equal(VerticalPositionValues.Superscript, runWithCaps.RunProperties!.VerticalTextAlignment?.Val?.Value);

            var sdt = paragraph.Descendants<SdtRun>().First();
            var sdtProps = sdt.SdtProperties;
            Assert.NotNull(sdtProps);
            Assert.Equal(123, sdtProps!.GetFirstChild<SdtId>()?.Val?.Value);
            Assert.Equal("cc-tag", sdtProps.GetFirstChild<Tag>()?.Val?.Value);
            Assert.Equal("cc-alias", sdtProps.GetFirstChild<SdtAlias>()?.Val?.Value);
            Assert.Equal(LockingValues.SdtContentLocked, sdtProps.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Lock>()?.Val?.Value);
            var placeholder = sdtProps.GetFirstChild<SdtPlaceholder>();
            Assert.Equal("PlaceholderId", placeholder?.DocPartReference?.Val?.Value);
            Assert.True(sdtProps.GetFirstChild<ShowingPlaceholder>()?.Val?.Value);
            var binding = sdtProps.GetFirstChild<DataBinding>();
            Assert.Equal("/root/item", binding?.XPath?.Value);
            Assert.Equal("{11111111-1111-1111-1111-111111111111}", binding?.StoreItemId?.Value);
            Assert.Equal("xmlns:ns='urn:example'", binding?.PrefixMappings?.Value);

            var footnotes = outputDoc.MainDocumentPart!.FootnotesPart?.Footnotes;
            Assert.NotNull(footnotes);
            Assert.Contains(footnotes!.Elements<Footnote>(), footnote => footnote.Id?.Value == 1);

            var endnotes = outputDoc.MainDocumentPart!.EndnotesPart?.Endnotes;
            Assert.NotNull(endnotes);
            Assert.Contains(endnotes!.Elements<Endnote>(), endnote => endnote.Id?.Value == 2);

            var comments = outputDoc.MainDocumentPart!.WordprocessingCommentsPart?.Comments;
            Assert.NotNull(comments);
            Assert.Contains(comments!.Elements<Comment>(), comment => comment.Id?.Value == "5");

            Assert.Contains(paragraph.Descendants<CommentRangeStart>(), item => item.Id?.Value == "5");
            Assert.Contains(paragraph.Descendants<CommentRangeEnd>(), item => item.Id?.Value == "5");
            Assert.Contains(paragraph.Descendants<CommentReference>(), item => item.Id?.Value == "5");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static void CreateInputDoc(string filePath)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        var runDefaults = new RunPropertiesBaseStyle(
            new RunFonts
            {
                Ascii = "Calibri",
                HighAnsi = "Calibri",
                AsciiTheme = ThemeFontValues.MinorAscii,
                HighAnsiTheme = ThemeFontValues.MinorHighAnsi
            },
            new FontSize { Val = "24" });
        var paragraphDefaults = new ParagraphPropertiesBaseStyle(
            new SpacingBetweenLines
            {
                After = "200",
                Line = "360",
                LineRule = LineSpacingRuleValues.Auto
            });
        styles.DocDefaults = new DocDefaults(
            new RunPropertiesDefault { RunPropertiesBaseStyle = runDefaults },
            new ParagraphPropertiesDefault { ParagraphPropertiesBaseStyle = paragraphDefaults });

        var basePara = new Style { Type = StyleValues.Paragraph, StyleId = "BasePara" };
        basePara.StyleName = new StyleName { Val = "BasePara" };
        basePara.AppendChild(new StyleRunProperties(new Color { Val = "FF0000" }));
        basePara.AppendChild(new StyleParagraphProperties(new SpacingBetweenLines { After = "200" }));
        styles.Append(basePara);

        var derivedPara = new Style { Type = StyleValues.Paragraph, StyleId = "DerivedPara" };
        derivedPara.StyleName = new StyleName { Val = "DerivedPara" };
        derivedPara.BasedOn = new BasedOn { Val = "BasePara" };
        derivedPara.AppendChild(new StyleRunProperties(new Bold()));
        styles.Append(derivedPara);

        var emphasis = new Style { Type = StyleValues.Character, StyleId = "Emphasis" };
        emphasis.StyleName = new StyleName { Val = "Emphasis" };
        emphasis.AppendChild(new StyleRunProperties(new Italic()));
        styles.Append(emphasis);

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();

        var paragraph = new Paragraph
        {
            ParagraphProperties = new ParagraphProperties(
                new ParagraphStyleId { Val = "DerivedPara" })
        };

        var run = new Run
        {
            RunProperties = new RunProperties(
                new RunStyle { Val = "Emphasis" },
                new Underline { Val = UnderlineValues.Dotted })
        };
        run.AppendChild(new Text("Styled text"));
        paragraph.AppendChild(run);
        mainPart.Document.Body!.AppendChild(paragraph);
        mainPart.Document.Save();
    }

    private static void CreateComplexInputDoc(string filePath)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        var footnotesPart = mainPart.AddNewPart<FootnotesPart>();
        footnotesPart.Footnotes = new Footnotes();
        footnotesPart.Footnotes.AppendChild(CreateSeparatorFootnote(-1, false));
        footnotesPart.Footnotes.AppendChild(CreateSeparatorFootnote(0, true));
        var footnote = new Footnote { Id = 1 };
        footnote.AppendChild(new Paragraph(new Run(new Text("Footnote body"))));
        footnotesPart.Footnotes.AppendChild(footnote);

        var endnotesPart = mainPart.AddNewPart<EndnotesPart>();
        endnotesPart.Endnotes = new Endnotes();
        endnotesPart.Endnotes.AppendChild(CreateEndnoteSeparator(-1));
        endnotesPart.Endnotes.AppendChild(CreateEndnoteSeparator(0));
        var endnote = new Endnote { Id = 2 };
        endnote.AppendChild(new Paragraph(new Run(new Text("Endnote body"))));
        endnotesPart.Endnotes.AppendChild(endnote);

        var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
        var comments = new Comments();
        var comment = new Comment
        {
            Id = "5",
            Author = "Author",
            Initials = "A",
            Date = DateTime.UtcNow
        };
        comment.AppendChild(new Paragraph(new Run(new Text("Comment body"))));
        comments.AppendChild(comment);
        commentsPart.Comments = comments;

        var paragraph = new Paragraph();
        var paragraphProperties = new ParagraphProperties();
        paragraphProperties.AppendChild(new BiDi { Val = true });
        paragraphProperties.AppendChild(new Shading
        {
            Val = ShadingPatternValues.Clear,
            Fill = "FFF2CC"
        });
        paragraphProperties.AppendChild(new ParagraphBorders(
            new TopBorder { Val = BorderValues.Single, Size = 8U, Color = "FF0000" },
            new BottomBorder { Val = BorderValues.Single, Size = 8U, Color = "00FF00" }));
        paragraph.ParagraphProperties = paragraphProperties;

        var styledRun = new Run(
            new RunProperties(
                new SmallCaps(),
                new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }),
            new Text("Caps"));
        paragraph.AppendChild(styledRun);

        var sdt = new SdtRun();
        var sdtProps = new SdtProperties(
            new SdtId { Val = 123 },
            new Tag { Val = "cc-tag" },
            new SdtAlias { Val = "cc-alias" },
            new DocumentFormat.OpenXml.Wordprocessing.Lock { Val = LockingValues.SdtContentLocked },
            new SdtPlaceholder(new DocPartReference { Val = "PlaceholderId" }),
            new ShowingPlaceholder { Val = true },
            new DataBinding
            {
                XPath = "/root/item",
                StoreItemId = "{11111111-1111-1111-1111-111111111111}",
                PrefixMappings = "xmlns:ns='urn:example'"
            });
        sdt.AppendChild(sdtProps);
        var sdtContent = new SdtContentRun();
        sdtContent.AppendChild(new Run(new Text("CC text")));
        sdt.AppendChild(sdtContent);
        paragraph.AppendChild(sdt);

        paragraph.AppendChild(new CommentRangeStart { Id = "5" });
        paragraph.AppendChild(new Run(new Text("Commented")));
        paragraph.AppendChild(new CommentRangeEnd { Id = "5" });
        paragraph.AppendChild(new Run(new CommentReference { Id = "5" }));

        paragraph.AppendChild(new Run(new FootnoteReference { Id = 1 }));
        paragraph.AppendChild(new Run(new EndnoteReference { Id = 2 }));

        mainPart.Document.Body!.AppendChild(paragraph);
        mainPart.Document.Save();
    }

    private static Footnote CreateSeparatorFootnote(int id, bool continuation)
    {
        var footnote = new Footnote { Id = id };
        var paragraph = new Paragraph();
        var run = new Run();
        if (continuation)
        {
            run.AppendChild(new ContinuationSeparatorMark());
        }
        else
        {
            run.AppendChild(new SeparatorMark());
        }

        paragraph.AppendChild(run);
        footnote.AppendChild(paragraph);
        return footnote;
    }

    private static Endnote CreateEndnoteSeparator(int id)
    {
        var endnote = new Endnote { Id = id };
        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run(new SeparatorMark()));
        endnote.AppendChild(paragraph);
        return endnote;
    }
}

using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProEdit.OpenXml;
using ProEditDocs = ProEdit.Documents;
using ProEditDocument = ProEdit.Documents.Document;
using Xunit;

namespace ProEdit.OpenXml.Tests;

public sealed class DocxInlineSpecialCharacterTests
{
    [Fact]
    public void Importer_ParsesNoBreakAndSoftHyphenRunElements()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProEditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateRunWithSpecialHyphenElements(inputPath);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            var paragraph = Assert.IsType<ProEditDocs.ParagraphBlock>(document.Blocks.First());
            Assert.Equal("Alpha\u2011Beta\u00ADGamma", ExtractParagraphText(paragraph));
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
    public void Importer_ParsesPositionalTabRunElements()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProEditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateRunWithPositionalTab(inputPath);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            var paragraph = Assert.IsType<ProEditDocs.ParagraphBlock>(document.Blocks.First());
            Assert.Equal("Left\tRight", ExtractParagraphText(paragraph));
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
    public void Importer_SplitsStandaloneLastRenderedPageBreakIntoPageBreakBlock()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProEditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");

        try
        {
            CreateRunWithStandaloneLastRenderedPageBreak(inputPath);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            Assert.True(document.Blocks.Count >= 3);
            var firstParagraph = Assert.IsType<ProEditDocs.ParagraphBlock>(document.Blocks[0]);
            Assert.IsType<ProEditDocs.PageBreakBlock>(document.Blocks[1]);
            var secondParagraph = Assert.IsType<ProEditDocs.ParagraphBlock>(document.Blocks[2]);

            Assert.Equal("Before", ExtractParagraphText(firstParagraph));
            Assert.Equal("After", ExtractParagraphText(secondParagraph));
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
    public void Exporter_WritesNoBreakAndSoftHyphenElements()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProEditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var outputPath = Path.Combine(tempRoot, "output.docx");

        try
        {
            var document = new ProEditDocument();
            document.Blocks.Clear();

            var paragraph = new ProEditDocs.ParagraphBlock();
            paragraph.Inlines.Add(new ProEditDocs.RunInline("Alpha\u2011Beta\u00ADGamma"));
            document.Blocks.Add(paragraph);

            var exporter = new DocxExporter();
            exporter.Save(document, outputPath);

            using (var outputDoc = WordprocessingDocument.Open(outputPath, false))
            {
                var body = outputDoc.MainDocumentPart!.Document!.Body!;
                Assert.Contains(body.Descendants<NoBreakHyphen>(), _ => true);
                Assert.Contains(body.Descendants<SoftHyphen>(), _ => true);
            }

            var importer = new DocxImporter();
            var imported = importer.Load(outputPath);
            var importedParagraph = Assert.IsType<ProEditDocs.ParagraphBlock>(imported.Blocks.First());
            Assert.Equal("Alpha\u2011Beta\u00ADGamma", ExtractParagraphText(importedParagraph));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static void CreateRunWithSpecialHyphenElements(string filePath)
    {
        using var doc = WordprocessingDocument.Create(filePath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        var run = new Run();
        run.AppendChild(new Text("Alpha"));
        run.AppendChild(new NoBreakHyphen());
        run.AppendChild(new Text("Beta"));
        run.AppendChild(new SoftHyphen());
        run.AppendChild(new Text("Gamma"));

        var paragraph = new Paragraph(run);
        mainPart.Document.Body!.AppendChild(paragraph);
        mainPart.Document.Save();
    }

    private static void CreateRunWithPositionalTab(string filePath)
    {
        using var doc = WordprocessingDocument.Create(filePath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        var run = new Run();
        run.AppendChild(new Text("Left"));
        run.AppendChild(new PositionalTab());
        run.AppendChild(new Text("Right"));

        mainPart.Document.Body!.AppendChild(new Paragraph(run));
        mainPart.Document.Save();
    }

    private static void CreateRunWithStandaloneLastRenderedPageBreak(string filePath)
    {
        using var doc = WordprocessingDocument.Create(filePath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        var paragraph = new Paragraph(
            new Run(new Text("Before")),
            new Run(new LastRenderedPageBreak()),
            new Run(new Text("After")));

        mainPart.Document.Body!.AppendChild(paragraph);
        mainPart.Document.Save();
    }

    private static string ExtractParagraphText(ProEditDocs.ParagraphBlock paragraph)
    {
        return string.Concat(paragraph.Inlines.OfType<ProEditDocs.RunInline>().Select(run => run.GetText()));
    }
}

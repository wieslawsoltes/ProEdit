using System;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Vibe.Office.Documents;
using Vibe.Office.OpenXml;
using Xunit;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace Vibe.Office.OpenXml.Tests;

public sealed class DocxAltChunkTests
{
    [Fact]
    public void Importer_ConvertsRtfAltChunkToBlocks()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "altchunk.docx");

        try
        {
            CreateRtfAltChunkDoc(inputPath, @"{\rtf1\ansi AltChunk text\par Second line}");

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            Assert.DoesNotContain(document.Blocks, block => block is AltChunkBlock);

            var paragraphs = document.Blocks.OfType<ParagraphBlock>().ToList();
            Assert.NotEmpty(paragraphs);

            var combined = string.Join("\n", paragraphs.Select(GetParagraphText));
            Assert.Contains("AltChunk text", combined, StringComparison.Ordinal);
            Assert.Contains("Second line", combined, StringComparison.Ordinal);
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
    public void Importer_ConvertsHtmlAltChunkToBlocks()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VibeOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "altchunk-html.docx");

        try
        {
            const string html = "<html><body><p>Alpha</p><p><b>Bold</b> Text</p></body></html>";
            CreateHtmlAltChunkDoc(inputPath, html);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);

            Assert.DoesNotContain(document.Blocks, block => block is AltChunkBlock);

            var paragraphs = document.Blocks.OfType<ParagraphBlock>().ToList();
            Assert.NotEmpty(paragraphs);

            var combined = string.Join("\n", paragraphs.Select(GetParagraphText));
            Assert.Contains("Alpha", combined, StringComparison.Ordinal);
            Assert.Contains("Bold Text", combined, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static void CreateRtfAltChunkDoc(string filePath, string rtf)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new WordDocument(new Body());

        var altPart = mainPart.AddAlternativeFormatImportPart("text/rtf");
        var data = Encoding.UTF8.GetBytes(rtf);
        using (var stream = altPart.GetStream(FileMode.Create, FileAccess.Write))
        {
            stream.Write(data, 0, data.Length);
        }

        var relId = mainPart.GetIdOfPart(altPart);
        mainPart.Document.Body!.AppendChild(new AltChunk { Id = relId });
        mainPart.Document.Save();
    }

    private static void CreateHtmlAltChunkDoc(string filePath, string html)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new WordDocument(new Body());

        var altPart = mainPart.AddAlternativeFormatImportPart("text/html");
        var data = Encoding.UTF8.GetBytes(html);
        using (var stream = altPart.GetStream(FileMode.Create, FileAccess.Write))
        {
            stream.Write(data, 0, data.Length);
        }

        var relId = mainPart.GetIdOfPart(altPart);
        mainPart.Document.Body!.AppendChild(new AltChunk { Id = relId });
        mainPart.Document.Save();
    }

    private static string GetParagraphText(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return paragraph.Text ?? string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run)
            {
                builder.Append(run.GetText());
            }
        }

        return builder.ToString();
    }
}

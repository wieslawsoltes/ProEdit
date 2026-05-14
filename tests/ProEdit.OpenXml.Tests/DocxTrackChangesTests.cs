using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProEdit.OpenXml;
using Xunit;

namespace ProEdit.OpenXml.Tests;

public sealed class DocxTrackChangesTests
{
    [Fact]
    public void RoundTrip_PreservesTrackChangeMarkup()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProEditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.docx");
        var outputPath = Path.Combine(tempRoot, "output.docx");

        try
        {
            CreateTrackedChangeDoc(inputPath);

            var importer = new DocxImporter();
            var document = importer.Load(inputPath);
            Assert.Contains(document.Revisions.Timeline, revision => revision.Kind == ProEdit.Documents.RevisionKind.Insert && revision.Id == 1);
            Assert.Contains(document.Revisions.Timeline, revision => revision.Kind == ProEdit.Documents.RevisionKind.Delete && revision.Id == 2);
            Assert.Contains(document.Revisions.Timeline, revision => revision.Kind == ProEdit.Documents.RevisionKind.MoveFrom && revision.Id == 3);
            Assert.Contains(document.Revisions.Timeline, revision => revision.Kind == ProEdit.Documents.RevisionKind.MoveTo && revision.Id == 4);

            var exporter = new DocxExporter();
            exporter.Save(document, outputPath);

            using var outputDoc = WordprocessingDocument.Open(outputPath, false);
            var paragraph = outputDoc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();

            Assert.Contains(paragraph.Descendants<InsertedRun>(), run => run.Id?.Value == "1");
            Assert.Contains(paragraph.Descendants<DeletedRun>(), run => run.Id?.Value == "2");
            Assert.Contains(paragraph.Descendants<MoveFromRun>(), run => run.Id?.Value == "3");
            Assert.Contains(paragraph.Descendants<MoveToRun>(), run => run.Id?.Value == "4");
            Assert.Contains(paragraph.Descendants<DeletedText>(), text => text.Text == "Deleted");
            Assert.Contains(paragraph.Descendants<MoveFromRangeStart>(), start => start.Id?.Value == "3" && start.Name?.Value == "MoveFrom");
            Assert.Contains(paragraph.Descendants<MoveToRangeStart>(), start => start.Id?.Value == "4" && start.Name?.Value == "MoveTo");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static void CreateTrackedChangeDoc(string filePath)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run(new Text("Base")));

        var inserted = new InsertedRun
        {
            Id = "1",
            Author = "Author",
            Date = DateTime.UtcNow
        };
        inserted.AppendChild(new Run(new Text("Inserted")));
        paragraph.AppendChild(inserted);

        var deleted = new DeletedRun
        {
            Id = "2",
            Author = "Author",
            Date = DateTime.UtcNow
        };
        deleted.AppendChild(new Run(new DeletedText("Deleted")));
        paragraph.AppendChild(deleted);

        var moveFromRangeStart = new MoveFromRangeStart
        {
            Id = "3",
            Author = "Author",
            Date = DateTime.UtcNow,
            Name = "MoveFrom"
        };
        var moveFromRangeEnd = new MoveFromRangeEnd { Id = "3" };
        var moveFrom = new MoveFromRun
        {
            Id = "3",
            Author = "Author",
            Date = DateTime.UtcNow
        };
        moveFrom.AppendChild(new Run(new Text("From")));
        paragraph.AppendChild(moveFromRangeStart);
        paragraph.AppendChild(moveFrom);
        paragraph.AppendChild(moveFromRangeEnd);

        var moveToRangeStart = new MoveToRangeStart
        {
            Id = "4",
            Author = "Author",
            Date = DateTime.UtcNow,
            Name = "MoveTo"
        };
        var moveToRangeEnd = new MoveToRangeEnd { Id = "4" };
        var moveTo = new MoveToRun
        {
            Id = "4",
            Author = "Author",
            Date = DateTime.UtcNow
        };
        moveTo.AppendChild(new Run(new Text("To")));
        paragraph.AppendChild(moveToRangeStart);
        paragraph.AppendChild(moveTo);
        paragraph.AppendChild(moveToRangeEnd);

        mainPart.Document.Body!.AppendChild(paragraph);
        mainPart.Document.Save();
    }
}

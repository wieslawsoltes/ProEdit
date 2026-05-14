using System.Linq;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Word.Editor;
using Xunit;

namespace ProEdit.Word.Editor.Tests;

public sealed class TrackChangesEditingTests
{
    [Fact]
    public void InsertText_WithTrackChanges_AddsRevisionMarkers()
    {
        var document = new Document { TrackChangesEnabled = true };
        var session = new EditorController(new EditorTestTextMeasurer(), document);

        session.InsertText("Hello");

        var paragraph = document.GetParagraph(0);
        Assert.Equal("Hello", DocumentEditHelpers.GetParagraphText(paragraph));
        Assert.Contains(paragraph.Inlines, inline => inline is RevisionStartInline start && start.Revision.Kind == RevisionKind.Insert);
        Assert.Contains(paragraph.Inlines, inline => inline is RevisionEndInline end && end.Kind == RevisionKind.Insert);

        var run = paragraph.Inlines.OfType<RunInline>().Single();
        Assert.Equal("Hello", run.GetText());

        var revision = Assert.Single(document.Revisions.Timeline);
        Assert.Equal(RevisionKind.Insert, revision.Kind);
        Assert.True(revision.Id.HasValue);
    }

    [Fact]
    public void Backspace_WithTrackChanges_WrapsDeletionRange()
    {
        var document = new Document();
        var session = new EditorController(new EditorTestTextMeasurer(), document);
        session.InsertText("Hello");
        document.TrackChangesEnabled = true;

        session.SetSelection(new TextRange(new TextPosition(0, 1), new TextPosition(0, 1)));
        session.Backspace();

        var paragraph = document.GetParagraph(0);
        Assert.Equal("Hello", DocumentEditHelpers.GetParagraphText(paragraph));
        Assert.Contains(paragraph.Inlines, inline => inline is RevisionStartInline start && start.Revision.Kind == RevisionKind.Delete);
        Assert.Contains(paragraph.Inlines, inline => inline is RevisionEndInline end && end.Kind == RevisionKind.Delete);

        var revision = Assert.Single(document.Revisions.Timeline);
        Assert.Equal(RevisionKind.Delete, revision.Kind);
    }
}

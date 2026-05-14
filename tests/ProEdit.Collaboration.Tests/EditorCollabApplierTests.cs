using ProEdit.Collaboration;
using ProEdit.Collaboration.Editor;
using ProEdit.Collaboration.Persistence;
using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Word.Editor;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class EditorCollabApplierTests
{
    [Fact]
    public void ApplyRemoteOps_UsesAuthorOverrideWhenTrackingChanges()
    {
        var document = new Document
        {
            TrackChangesEnabled = true
        };
        var session = new EditorController(new StubTextMeasurer(), document);
        var applier = new EditorCollabApplier(session);
        var paragraph = document.GetParagraph(0);

        applier.ApplyRemoteOps(new ICollabOp[]
        {
            new InsertTextOp(TextAnchor.Before(paragraph.NodeId, 0), "Remote")
        }, "RemoteUser");

        Assert.Contains(document.Revisions.Timeline, revision => revision.Author == "RemoteUser");
    }

    [Fact]
    public void ApplyRemoteOps_InsertsAndReplacesBlocks()
    {
        var document = new Document();
        var session = new EditorController(new StubTextMeasurer(), document);
        var applier = new EditorCollabApplier(session);
        var serializer = new CollabBlockSerializer();

        var inserted = new ParagraphBlock("Inserted");
        var insertPayload = serializer.Serialize(inserted, document);

        applier.ApplyRemoteOps(new ICollabOp[]
        {
            new InsertBlockOp(CollabContainerIds.Body, CollabPositionToken.FromIndex(1), nameof(ParagraphBlock), insertPayload)
        });

        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal("Inserted", ((ParagraphBlock)document.Blocks[1]).Text);

        var replacement = new ParagraphBlock("Replaced") { NodeId = document.Blocks[1].NodeId };
        var replacePayload = serializer.Serialize(replacement, document);

        applier.ApplyRemoteOps(new ICollabOp[]
        {
            new ReplaceBlockOp(replacement.NodeId, replacePayload)
        });

        Assert.Equal("Replaced", ((ParagraphBlock)document.Blocks[1]).Text);

        applier.ApplyRemoteOps(new ICollabOp[]
        {
            new DeleteBlockOp(CollabContainerIds.Body, CollabPositionToken.FromIndex(1), replacement.NodeId)
        });

        Assert.Single(document.Blocks);
    }

    [Fact]
    public void ApplyRemoteOps_AppliesPropertyOps()
    {
        var document = new Document();
        var session = new EditorController(new StubTextMeasurer(), document);
        var applier = new EditorCollabApplier(session);
        var paragraph = document.GetParagraph(0);
        paragraph.Inlines.Clear();
        var run = new RunInline("Text") { StyleId = "OldStyle" };
        paragraph.Inlines.Add(run);

        applier.ApplyRemoteOps(new ICollabOp[]
        {
            new SetParagraphPropertiesOp(paragraph.NodeId, new Dictionary<string, string>
            {
                { "styleId", "Heading1" }
            }, 1),
            new SetInlinePropertiesOp(run.NodeId, new Dictionary<string, string>
            {
                { "styleId", "Emphasis" }
            }, 2)
        });

        Assert.Equal("Heading1", paragraph.StyleId);
        Assert.Equal("Emphasis", run.StyleId);
    }

    private sealed class StubTextMeasurer : ITextMeasurer
    {
        public TextMetrics MeasureText(string text, TextStyle style)
        {
            var width = string.IsNullOrEmpty(text) ? 0f : text.Length * 5f;
            var height = style.FontSize <= 0f ? 10f : style.FontSize;
            return new TextMetrics(width, height, height * 0.8f, height * 0.2f);
        }
    }
}

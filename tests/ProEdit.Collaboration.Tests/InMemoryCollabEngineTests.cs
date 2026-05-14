using ProEdit.Collaboration;
using ProEdit.Documents;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class InMemoryCollabEngineTests
{
    [Fact]
    public void Apply_InsertAndDeleteText_UpdatesDocument()
    {
        var document = new Document();
        var paragraph = document.GetParagraph(0);
        paragraph.Text = string.Empty;

        var engine = new InMemoryCollabEngine(document);
        var actorId = Guid.NewGuid();

        var insertAnchor = TextAnchor.Before(paragraph.NodeId, 0);
        var insertBatch = CollabOpBatch.Create(actorId, 0, 1, 1, new ICollabOp[]
        {
            new InsertTextOp(insertAnchor, "Hello World")
        });

        engine.Apply(insertBatch, CollabApplyOrigin.Local);

        Assert.Equal("Hello World", engine.Document.GetParagraph(0).Text);

        var deleteBatch = CollabOpBatch.Create(actorId, engine.Version, 2, 2, new ICollabOp[]
        {
            new DeleteRangeOp(TextAnchor.Before(paragraph.NodeId, 6), TextAnchor.Before(paragraph.NodeId, 11))
        });

        engine.Apply(deleteBatch, CollabApplyOrigin.Local);

        Assert.Equal("Hello ", engine.Document.GetParagraph(0).Text);
    }

    [Fact]
    public void Apply_RespectsOperationOrder()
    {
        var document = new Document();
        var paragraph = document.GetParagraph(0);
        paragraph.Text = string.Empty;

        var engine = new InMemoryCollabEngine(document);
        var actorId = Guid.NewGuid();

        var batch = CollabOpBatch.Create(actorId, 0, 1, 1, new ICollabOp[]
        {
            new InsertTextOp(TextAnchor.Before(paragraph.NodeId, 0), "B"),
            new InsertTextOp(TextAnchor.Before(paragraph.NodeId, 0), "A")
        });

        engine.Apply(batch, CollabApplyOrigin.Local);

        Assert.Equal("AB", engine.Document.GetParagraph(0).Text);
    }

    [Fact]
    public void Apply_ParagraphAndInlineProperties()
    {
        var document = new Document();
        var paragraph = document.GetParagraph(0);
        paragraph.Inlines.Clear();
        var run = new RunInline("Text") { StyleId = "OldStyle" };
        paragraph.Inlines.Add(run);

        var engine = new InMemoryCollabEngine(document);
        var actorId = Guid.NewGuid();

        var batch = CollabOpBatch.Create(actorId, 0, 1, 1, new ICollabOp[]
        {
            new SetParagraphPropertiesOp(paragraph.NodeId, new Dictionary<string, string>
            {
                { "styleId", "Heading2" }
            }, 1),
            new SetInlinePropertiesOp(run.NodeId, new Dictionary<string, string>
            {
                { "styleId", "Strong" }
            }, 2)
        });

        engine.Apply(batch, CollabApplyOrigin.Local);

        Assert.Equal("Heading2", engine.Document.GetParagraph(0).StyleId);
        var updatedRun = Assert.IsType<RunInline>(engine.Document.GetParagraph(0).Inlines[0]);
        Assert.Equal("Strong", updatedRun.StyleId);
    }
}

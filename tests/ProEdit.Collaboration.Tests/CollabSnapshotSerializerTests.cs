using ProEdit.Collaboration;
using ProEdit.Collaboration.Persistence;
using ProEdit.Documents;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class CollabSnapshotSerializerTests
{
    [Fact]
    public void SnapshotSerializer_RoundTripsSimpleDocument()
    {
        var document = new Document();
        var paragraph = document.GetParagraph(0);
        paragraph.Text = string.Empty;
        paragraph.Inlines.Clear();
        var run = new RunInline("Hello");
        paragraph.Inlines.Add(run);

        var snapshot = CollabSnapshot.Create(12, document);
        var serializer = new CollabSnapshotSerializer();

        var payload = serializer.Serialize(snapshot);
        var loaded = serializer.DeserializeSnapshot(payload);

        Assert.Equal(12, loaded.Version);
        Assert.Equal("Hello", loaded.Document.GetParagraph(0).Text);
        Assert.Equal(paragraph.NodeId, loaded.Document.GetParagraph(0).NodeId);
        var loadedRun = Assert.IsType<RunInline>(loaded.Document.GetParagraph(0).Inlines[0]);
        Assert.Equal(run.NodeId, loadedRun.NodeId);
    }
}

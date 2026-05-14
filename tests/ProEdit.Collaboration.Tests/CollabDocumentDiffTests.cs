using ProEdit.Collaboration;
using ProEdit.Documents;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class CollabDocumentDiffTests
{
    [Fact]
    public void DiffDetectsListInfoChanges()
    {
        var before = new Document();
        var after = DocumentClone.Clone(before);

        var paragraph = after.GetParagraph(0);
        paragraph.ListInfo = new ListInfo(ListKind.Bullet, level: 0);

        var diff = new CollabDocumentDiff();
        var changed = diff.TryBuildOps(before, after, out var forward, out var inverse);

        Assert.True(changed);
        Assert.NotEmpty(forward);
        Assert.NotEmpty(inverse);
        Assert.Contains(forward, op => op is ReplaceBlockOp);
    }

    [Fact]
    public void DiffDetectsResourceChanges()
    {
        var before = new Document();
        var after = DocumentClone.Clone(before);

        after.MirrorMargins = true;

        var diff = new CollabDocumentDiff();
        var changed = diff.TryBuildOps(before, after, out var forward, out var inverse);

        Assert.True(changed);
        Assert.NotEmpty(forward);
        Assert.NotEmpty(inverse);
        Assert.Contains(forward, op => op is ReplaceDocumentResourcesOp);
    }
}

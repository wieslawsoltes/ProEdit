using Vibe.Office.Collaboration;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class CollabOpHistoryTests
{
    [Fact]
    public void TransformRemote_InsertSameOffset_OrdersByActorId()
    {
        var history = new CollabOpHistory();
        var nodeId = Guid.NewGuid();
        var actorA = new Guid("00000000-0000-0000-0000-000000000001");
        var actorB = new Guid("00000000-0000-0000-0000-000000000002");

        var localBatch = CollabOpBatch.Create(actorA, 0, 1, 1, new ICollabOp[]
        {
            new InsertTextOp(TextAnchor.Before(nodeId, 0), "A")
        });

        history.AppendLocal(localBatch);

        var remoteBatch = CollabOpBatch.Create(actorB, 0, 1, 1, new ICollabOp[]
        {
            new InsertTextOp(TextAnchor.Before(nodeId, 0), "B")
        });

        var result = history.TransformRemote(remoteBatch);

        Assert.False(result.RequiresResync);
        var transformed = Assert.Single(result.Ops);
        var insert = Assert.IsType<InsertTextOp>(transformed);
        Assert.Equal(1, insert.Anchor.Offset);
    }

    [Fact]
    public void TransformRemote_IgnoresDuplicateBatchIds()
    {
        var history = new CollabOpHistory();
        var nodeId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        var batch = new CollabOpBatch(batchId, actor, 0, 1, 1, DateTimeOffset.UtcNow, new ICollabOp[]
        {
            new InsertTextOp(TextAnchor.Before(nodeId, 0), "Hi")
        });

        var first = history.TransformRemote(batch);
        Assert.Single(first.Ops);

        var second = history.TransformRemote(batch);
        Assert.Empty(second.Ops);
        Assert.False(second.RequiresResync);
    }
}

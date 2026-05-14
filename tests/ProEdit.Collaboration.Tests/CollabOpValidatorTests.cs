using ProEdit.Collaboration;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class CollabOpValidatorTests
{
    [Fact]
    public void ValidateOp_FailsForEmptyInsertText()
    {
        var op = new InsertTextOp(TextAnchor.Before(Guid.NewGuid(), 0), string.Empty);

        var result = CollabOpValidator.ValidateOp(op, out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateBatch_FailsForMissingActor()
    {
        var op = new InsertTextOp(TextAnchor.Before(Guid.NewGuid(), 0), "Hello");
        var batch = new CollabOpBatch(Guid.NewGuid(), Guid.Empty, 0, 0, 0, DateTimeOffset.UtcNow, new ICollabOp[] { op });

        var result = CollabOpValidator.ValidateBatch(batch, out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateOp_FailsForReplaceBlockWithoutPayload()
    {
        var op = new ReplaceBlockOp(Guid.NewGuid(), Array.Empty<byte>());

        var result = CollabOpValidator.ValidateOp(op, out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }
}

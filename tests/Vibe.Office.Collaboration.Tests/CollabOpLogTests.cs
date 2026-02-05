using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Persistence;
using Xunit;
using System.Linq;
using System.IO;

namespace Vibe.Office.Collaboration.Tests;

public sealed class CollabOpLogTests
{
    [Fact]
    public void OpLog_AppendAndRead_RoundTripsBatch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var logPath = Path.Combine(tempDir, "session");

        var batch = CollabOpBatch.Create(Guid.NewGuid(), 0, 1, 1, new ICollabOp[]
        {
            new InsertTextOp(TextAnchor.Before(Guid.NewGuid(), 0), "Hello")
        });

        using (var writer = new CollabOpLogWriter(logPath + CollabPersistedFormat.OpLogExtension))
        {
            writer.Append(batch);
        }

        using var reader = new CollabOpLogReader(logPath + CollabPersistedFormat.OpLogExtension);
        var batches = reader.ReadAll().ToList();

        Assert.Single(batches);
        Assert.Equal(batch.ActorId, batches[0].ActorId);
        Assert.Equal(batch.Ops.Count, batches[0].Ops.Count);
    }
}

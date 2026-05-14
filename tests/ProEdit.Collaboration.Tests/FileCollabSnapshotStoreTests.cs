using ProEdit.Collaboration;
using ProEdit.Collaboration.Persistence;
using ProEdit.Documents;
using Xunit;
using System.IO;

namespace ProEdit.Collaboration.Tests;

public sealed class FileCollabSnapshotStoreTests
{
    [Fact]
    public async Task SnapshotStore_CompactsOpLog()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "session");

        var store = new FileCollabSnapshotStore(basePath, new CollabSnapshotStoreOptions(LogSizeThresholdBytes: 1, TailOpCount: 0));
        var document = new Document();
        var snapshot = CollabSnapshot.Create(0, document);
        await store.WriteSnapshotAsync(snapshot);

        var paragraph = document.GetParagraph(0);
        var batch = CollabOpBatch.Create(Guid.NewGuid(), 0, 1, 1, new ICollabOp[]
        {
            new InsertTextOp(TextAnchor.Before(paragraph.NodeId, 0), "Hi")
        });

        await store.AppendOpsAsync(batch);

        await store.CompactAsync();

        Assert.False(File.Exists(basePath + CollabPersistedFormat.OpLogExtension));
        Assert.True(File.Exists(basePath + CollabPersistedFormat.SnapshotExtension));
    }

    [Fact]
    public async Task SnapshotStore_HandlesManyOps()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "session");

        var store = new FileCollabSnapshotStore(basePath, new CollabSnapshotStoreOptions(LogSizeThresholdBytes: 1024));
        var document = new Document();
        var snapshot = CollabSnapshot.Create(0, document);
        await store.WriteSnapshotAsync(snapshot);

        var paragraph = document.GetParagraph(0);
        for (var i = 0; i < 500; i++)
        {
            var batch = CollabOpBatch.Create(Guid.NewGuid(), i, i + 1, i + 1, new ICollabOp[]
            {
                new InsertTextOp(TextAnchor.Before(paragraph.NodeId, 0), "x")
            });
            await store.AppendOpsAsync(batch);
        }

        await store.CompactAsync();

        Assert.True(File.Exists(basePath + CollabPersistedFormat.SnapshotExtension));
    }
}

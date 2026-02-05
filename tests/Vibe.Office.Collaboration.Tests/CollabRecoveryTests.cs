using System.IO;
using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Persistence;
using Vibe.Office.Documents;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class CollabRecoveryTests
{
    [Fact]
    public async Task Recovery_ReplaysTailOps()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "session");

        var store = new FileCollabSnapshotStore(basePath, new CollabSnapshotStoreOptions(TailOpCount: 1));
        var document = new Document();
        var snapshot = CollabSnapshot.Create(0, document);
        await store.WriteSnapshotAsync(snapshot);

        var paragraph = document.GetParagraph(0);
        var batch = CollabOpBatch.Create(Guid.NewGuid(), 0, 1, 1, new ICollabOp[]
        {
            new InsertTextOp(TextAnchor.Before(paragraph.NodeId, 0), "Hi")
        });

        await store.AppendOpsAsync(batch);

        var result = await CollabRecovery.RecoverAsync(basePath);
        var recoveredParagraph = result.Document.GetParagraph(0);

        Assert.Contains("Hi", recoveredParagraph.Text ?? string.Empty);
    }
}

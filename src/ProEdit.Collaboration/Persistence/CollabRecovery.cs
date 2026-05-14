using System.IO;
using ProEdit.Documents;

namespace ProEdit.Collaboration.Persistence;

public sealed record CollabRecoveryResult(Document Document, long Version, Guid? SnapshotId);

public static class CollabRecovery
{
    public static async ValueTask<CollabRecoveryResult> RecoverAsync(string basePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basePath);

        var store = new FileCollabSnapshotStore(basePath);
        var snapshot = await store.LoadLatestSnapshotAsync(cancellationToken);
        var document = snapshot?.Document ?? new Document();
        var version = snapshot?.Version ?? 0;

        var engine = new InMemoryCollabEngine(document, version);
        var opLogPath = basePath + CollabPersistedFormat.OpLogExtension;
        if (File.Exists(opLogPath))
        {
            using var reader = new CollabOpLogReader(opLogPath);
            foreach (var batch in reader.ReadAll())
            {
                engine.Apply(batch, CollabApplyOrigin.Remote);
            }
        }

        return new CollabRecoveryResult(engine.Document, engine.Version, snapshot?.SnapshotId);
    }
}

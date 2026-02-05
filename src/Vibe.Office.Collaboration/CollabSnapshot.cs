using Vibe.Office.Documents;

namespace Vibe.Office.Collaboration;

/// <summary>
/// Represents a collaboration snapshot containing a document state and metadata.
/// </summary>
public sealed record CollabSnapshot(Guid SnapshotId, long Version, Document Document, DateTimeOffset CreatedAtUtc)
{
    public static CollabSnapshot Create(long version, Document document)
    {
        return new CollabSnapshot(Guid.NewGuid(), version, document, DateTimeOffset.UtcNow);
    }
}

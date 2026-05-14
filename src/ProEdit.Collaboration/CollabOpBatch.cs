namespace ProEdit.Collaboration;

/// <summary>
/// Groups a set of operations with causal metadata.
/// </summary>
public sealed record CollabOpBatch(
    Guid BatchId,
    Guid ActorId,
    long BaseVersion,
    long Sequence,
    long Lamport,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<ICollabOp> Ops)
{
    /// <summary>
    /// Creates a new batch with a generated batch id and UTC timestamp.
    /// </summary>
    public static CollabOpBatch Create(Guid actorId, long baseVersion, long sequence, long lamport, IReadOnlyList<ICollabOp> ops)
    {
        return new CollabOpBatch(Guid.NewGuid(), actorId, baseVersion, sequence, lamport, DateTimeOffset.UtcNow, ops);
    }
}

using System.Threading;

namespace ProEdit.Collaboration;

/// <summary>
/// Creates collaboration op batches with consistent sequencing and base versions.
/// </summary>
public interface ICollabBatchFactory
{
    /// <summary>
    /// Creates a new op batch for the supplied operations.
    /// </summary>
    CollabOpBatch Create(IReadOnlyList<ICollabOp> ops);
}

/// <summary>
/// Default batch factory implementation.
/// </summary>
public sealed class CollabBatchFactory : ICollabBatchFactory
{
    private readonly Guid _actorId;
    private readonly Func<long> _baseVersionProvider;
    private long _sequence;
    private long _lamport;

    public CollabBatchFactory(Guid actorId, Func<long> baseVersionProvider)
    {
        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("ActorId is required.", nameof(actorId));
        }

        _actorId = actorId;
        _baseVersionProvider = baseVersionProvider ?? throw new ArgumentNullException(nameof(baseVersionProvider));
    }

    public CollabOpBatch Create(IReadOnlyList<ICollabOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);

        var baseVersion = _baseVersionProvider();
        var sequence = Interlocked.Increment(ref _sequence);
        var lamport = Interlocked.Increment(ref _lamport);

        return CollabOpBatch.Create(_actorId, baseVersion, sequence, lamport, ops);
    }
}

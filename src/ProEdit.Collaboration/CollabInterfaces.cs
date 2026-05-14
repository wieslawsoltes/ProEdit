namespace ProEdit.Collaboration;

/// <summary>
/// Defines the origin of an operation batch.
/// </summary>
public enum CollabApplyOrigin
{
    Local,
    Remote
}

/// <summary>
/// Represents the result of applying a batch of operations.
/// </summary>
public sealed record CollabApplyResult(long Version, IReadOnlyList<ICollabOp> AppliedOps);

/// <summary>
/// Defines the collaboration engine used to apply operations.
/// </summary>
public interface ICollabEngine
{
    /// <summary>
    /// Gets the current logical version of the document.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// Applies a batch of operations and returns the result.
    /// </summary>
    CollabApplyResult Apply(CollabOpBatch batch, CollabApplyOrigin origin);
}

/// <summary>
/// Defines a collaboration session lifecycle.
/// </summary>
public interface ICollabSession
{
    /// <summary>
    /// Connects the session to the collaboration transport.
    /// </summary>
    ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the session from the collaboration transport.
    /// </summary>
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a local batch for propagation.
    /// </summary>
    ValueTask SubmitLocalAsync(CollabOpBatch batch, CancellationToken cancellationToken = default);
}

/// <summary>
/// Transport abstraction for collaboration messages.
/// </summary>
public interface ICollabTransport
{
    /// <summary>
    /// Raised when a message payload is received.
    /// </summary>
    event EventHandler<CollabTransportMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Raised when the transport state changes.
    /// </summary>
    event EventHandler<CollabTransportStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Sends a raw message payload.
    /// </summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// Transport abstraction that supports explicit connect and disconnect operations.
/// </summary>
public interface ICollabTransportConnection : ICollabTransport
{
    /// <summary>
    /// Connects the transport.
    /// </summary>
    ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the transport.
    /// </summary>
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Snapshot store abstraction for persisted collaboration state.
/// </summary>
public interface ICollabSnapshotStore
{
    /// <summary>
    /// Loads the latest snapshot if available.
    /// </summary>
    ValueTask<CollabSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an op batch to the persistent log.
    /// </summary>
    ValueTask AppendOpsAsync(CollabOpBatch batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a new snapshot to the store.
    /// </summary>
    ValueTask WriteSnapshotAsync(CollabSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compacts the log and snapshots.
    /// </summary>
    ValueTask CompactAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Event args for transport message delivery.
/// </summary>
public sealed class CollabTransportMessageEventArgs : EventArgs
{
    public CollabTransportMessageEventArgs(ReadOnlyMemory<byte> payload)
    {
        Payload = payload;
    }

    public ReadOnlyMemory<byte> Payload { get; }
}

/// <summary>
/// Transport connection state.
/// </summary>
public enum CollabTransportState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// Event args for transport state changes.
/// </summary>
public sealed class CollabTransportStateChangedEventArgs : EventArgs
{
    public CollabTransportStateChangedEventArgs(CollabTransportState state, string? message = null)
    {
        State = state;
        Message = message;
    }

    public CollabTransportState State { get; }
    public string? Message { get; }
}

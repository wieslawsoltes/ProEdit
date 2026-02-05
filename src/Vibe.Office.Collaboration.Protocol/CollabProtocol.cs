using Vibe.Office.Collaboration;

namespace Vibe.Office.Collaboration.Protocol;

/// <summary>
/// Defines protocol versions for collaboration messages.
/// </summary>
public static class CollabProtocolVersion
{
    public const int V1 = 1;
}

/// <summary>
/// Identifies the type of collaboration message.
/// </summary>
public enum CollabMessageType
{
    Hello,
    Join,
    Snapshot,
    Ops,
    Ack,
    Presence,
    Error,
    Leave
}

/// <summary>
/// Base envelope for collaboration messages.
/// </summary>
public sealed record CollabEnvelope<TPayload>(
    int ProtocolVersion,
    Guid DocumentId,
    Guid SessionId,
    Guid SenderId,
    long Sequence,
    long Lamport,
    DateTimeOffset TimestampUtc,
    CollabMessageType MessageType,
    TPayload Payload);

/// <summary>
/// Client capability handshake.
/// </summary>
public sealed record HelloMessage(string ClientName, IReadOnlyList<string> Capabilities, string? Compression);

/// <summary>
/// Join request for a collaboration session.
/// </summary>
public sealed record JoinMessage(Guid DocumentId, long KnownVersion, Guid? SnapshotId);

/// <summary>
/// Snapshot payload containing serialized document bytes.
/// </summary>
public sealed record SnapshotMessage(Guid SnapshotId, long Version, byte[] Payload);

/// <summary>
/// Operations payload.
/// </summary>
public sealed record OpsMessage(CollabOpBatch Batch);

/// <summary>
/// Acknowledgment payload.
/// </summary>
public sealed record AckMessage(Guid BatchId, Guid ActorId, long Sequence);

/// <summary>
/// Presence payload.
/// </summary>
public sealed record PresenceMessage(PresenceState Presence, TimeSpan TimeToLive);

/// <summary>
/// Error payload.
/// </summary>
public sealed record ErrorMessage(string Code, string Message);

/// <summary>
/// Leave payload.
/// </summary>
public sealed record LeaveMessage(string? Reason = null);

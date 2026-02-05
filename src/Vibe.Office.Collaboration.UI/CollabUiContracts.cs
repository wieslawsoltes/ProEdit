using Vibe.Office.Collaboration;

namespace Vibe.Office.Collaboration.UI;

/// <summary>
/// Represents the current collaboration connection state.
/// </summary>
public enum CollabConnectionState
{
    /// <summary>
    /// The collaboration session is disconnected.
    /// </summary>
    Disconnected,

    /// <summary>
    /// The collaboration session is attempting to connect.
    /// </summary>
    Connecting,

    /// <summary>
    /// The collaboration session is connected.
    /// </summary>
    Connected,

    /// <summary>
    /// The collaboration session is reconnecting after a transient failure.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// The collaboration session is in an error state.
    /// </summary>
    Error,

    /// <summary>
    /// The collaboration session is offline by user choice or network policy.
    /// </summary>
    Offline
}

/// <summary>
/// Identifies the transport mode used for collaboration.
/// </summary>
public enum CollabTransportMode
{
    /// <summary>
    /// Use a local broker for LAN or same-machine sessions.
    /// </summary>
    LocalBroker,

    /// <summary>
    /// Use a shared file sidecar for offline collaboration.
    /// </summary>
    SharedFile,

    /// <summary>
    /// Use an automatic shared file path derived from the document or session.
    /// </summary>
    SharedFileAuto,

    /// <summary>
    /// Use a server relay (WebSocket) for collaboration.
    /// </summary>
    Server
}

/// <summary>
/// Describes a collaborator in the current session.
/// </summary>
public sealed record CollabParticipant(
    Guid UserId,
    string DisplayName,
    string Color,
    DateTimeOffset LastActiveUtc,
    bool IsLocal);

/// <summary>
/// Provides identity details for the local collaborator.
/// </summary>
public interface ICollabIdentityService
{
    /// <summary>
    /// Gets the stable identifier for the local user.
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// Gets the display name for the local user.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the preferred color for the local user.
    /// </summary>
    string Color { get; }
}

/// <summary>
/// Exposes collaboration state for UI integration.
/// </summary>
public interface ICollabUiService
{
    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    CollabConnectionState ConnectionState { get; }

    /// <summary>
    /// Gets the latest connection message or error details.
    /// </summary>
    string? ConnectionMessage { get; }

    /// <summary>
    /// Gets the document identifier for the current session.
    /// </summary>
    Guid DocumentId { get; }

    /// <summary>
    /// Gets the session identifier for the current connection.
    /// </summary>
    Guid SessionId { get; }

    /// <summary>
    /// Gets or sets the transport mode for collaboration.
    /// </summary>
    CollabTransportMode TransportMode { get; set; }

    /// <summary>
    /// Gets or sets the server URL when <see cref="TransportMode"/> is <see cref="CollabTransportMode.Server"/>.
    /// </summary>
    string? ServerUrl { get; set; }

    /// <summary>
    /// Gets or sets the shared path when <see cref="TransportMode"/> is <see cref="CollabTransportMode.SharedFile"/>.
    /// </summary>
    string? SharedPath { get; set; }

    /// <summary>
    /// Gets the resolved shared path used by the current transport mode.
    /// </summary>
    string? ResolvedSharedPath { get; }

    /// <summary>
    /// Gets or sets the local broker port when <see cref="TransportMode"/> is <see cref="CollabTransportMode.LocalBroker"/>.
    /// </summary>
    int? LocalBrokerPort { get; set; }

    /// <summary>
    /// Gets the current list of collaborators.
    /// </summary>
    IReadOnlyList<CollabParticipant> Participants { get; }

    /// <summary>
    /// Gets the latest presence states.
    /// </summary>
    IReadOnlyList<PresenceState> Presence { get; }

    /// <summary>
    /// Gets the estimated synchronization lag.
    /// </summary>
    TimeSpan SyncLag { get; }

    /// <summary>
    /// Gets the depth of the outbound operation queue.
    /// </summary>
    int OpQueueDepth { get; }

    /// <summary>
    /// Gets the age of the latest snapshot.
    /// </summary>
    TimeSpan SnapshotAge { get; }

    /// <summary>
    /// Raised whenever the collaboration state changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Initiates a collaboration session.
    /// </summary>
    ValueTask JoinAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Leaves the current collaboration session.
    /// </summary>
    ValueTask LeaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a share flow for the current session.
    /// </summary>
    ValueTask ShareAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to reconnect after a failure.
    /// </summary>
    ValueTask ReconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the presence registry with a new state.
    /// </summary>
    void UpdatePresence(PresenceState presence, TimeSpan? timeToLive = null);

    /// <summary>
    /// Replaces the list of active participants.
    /// </summary>
    void UpdateParticipants(IReadOnlyList<CollabParticipant> participants);

    /// <summary>
    /// Updates diagnostics metrics for the session.
    /// </summary>
    void UpdateDiagnostics(TimeSpan syncLag, int opQueueDepth, TimeSpan snapshotAge);

    /// <summary>
    /// Updates the connection state and optional message.
    /// </summary>
    void SetConnectionState(CollabConnectionState state, string? message = null);

    /// <summary>
    /// Clears any active error message.
    /// </summary>
    void ClearError();
}

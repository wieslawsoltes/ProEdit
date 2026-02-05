using System.Text.Json;
using Vibe.Office.Collaboration;

namespace Vibe.Office.Collaboration.Protocol;

/// <summary>
/// Options for configuring a realtime collaboration session.
/// </summary>
public sealed class CollabRealtimeSessionOptions
{
    /// <summary>
    /// Gets the document identifier.
    /// </summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// Gets the session identifier.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// Gets the sender identifier for this session.
    /// </summary>
    public Guid SenderId { get; init; }

    /// <summary>
    /// Gets the client name for protocol hello.
    /// </summary>
    public string ClientName { get; init; } = "Vibe Office";

    /// <summary>
    /// Gets the list of capabilities advertised to the server.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = new[] { "ops", "presence", "snapshot" };

    /// <summary>
    /// Gets the compression identifier for the session.
    /// </summary>
    public string? Compression { get; init; }

    /// <summary>
    /// Gets the minimum payload size before compression is attempted.
    /// </summary>
    public int CompressionThresholdBytes { get; init; } = CollabMessageCompression.DefaultThresholdBytes;

    /// <summary>
    /// Gets the maximum allowed decompressed payload size.
    /// </summary>
    public int MaxDecompressedBytes { get; init; } = CollabMessageCompression.DefaultMaxDecompressedBytes;

    /// <summary>
    /// Gets the default presence time to live.
    /// </summary>
    public TimeSpan DefaultPresenceTimeToLive { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the minimum interval between presence messages.
    /// </summary>
    public TimeSpan PresenceThrottleInterval { get; init; } = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// Gets the last known document version.
    /// </summary>
    public long KnownVersion { get; init; }

    /// <summary>
    /// Gets the last known snapshot identifier.
    /// </summary>
    public Guid? SnapshotId { get; init; }
}

/// <summary>
/// Realtime collaboration session with protocol handling and transport integration.
/// </summary>
public interface ICollabRealtimeSession : ICollabSession
{
    /// <summary>
    /// Gets the document identifier for the session.
    /// </summary>
    Guid DocumentId { get; }

    /// <summary>
    /// Gets the session identifier.
    /// </summary>
    Guid SessionId { get; }

    /// <summary>
    /// Gets the sender identifier used in protocol envelopes.
    /// </summary>
    Guid SenderId { get; }

    /// <summary>
    /// Raised when the underlying transport changes state.
    /// </summary>
    event EventHandler<CollabTransportStateChangedEventArgs>? TransportStateChanged;

    /// <summary>
    /// Raised when a remote op batch is received.
    /// </summary>
    event EventHandler<CollabOpsReceivedEventArgs>? OpsReceived;

    /// <summary>
    /// Raised when a snapshot payload is received.
    /// </summary>
    event EventHandler<CollabSnapshotReceivedEventArgs>? SnapshotReceived;

    /// <summary>
    /// Raised when a presence update is received.
    /// </summary>
    event EventHandler<CollabPresenceEventArgs>? PresenceReceived;

    /// <summary>
    /// Raised when a protocol error is received.
    /// </summary>
    event EventHandler<CollabErrorEventArgs>? ErrorReceived;

    /// <summary>
    /// Sends a presence update.
    /// </summary>
    ValueTask SendPresenceAsync(PresenceState presence, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a snapshot payload.
    /// </summary>
    ValueTask SendSnapshotAsync(SnapshotMessage snapshot, CancellationToken cancellationToken = default);
}

/// <summary>
/// Event args for remote op batches.
/// </summary>
public sealed class CollabOpsReceivedEventArgs : EventArgs
{
    public CollabOpsReceivedEventArgs(CollabOpBatch batch, DateTimeOffset timestampUtc)
    {
        Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        TimestampUtc = timestampUtc;
    }

    public CollabOpBatch Batch { get; }
    public DateTimeOffset TimestampUtc { get; }
}

/// <summary>
/// Event args for presence updates.
/// </summary>
public sealed class CollabPresenceEventArgs : EventArgs
{
    public CollabPresenceEventArgs(PresenceState presence, TimeSpan timeToLive, DateTimeOffset timestampUtc)
    {
        Presence = presence ?? throw new ArgumentNullException(nameof(presence));
        TimeToLive = timeToLive;
        TimestampUtc = timestampUtc;
    }

    public PresenceState Presence { get; }
    public TimeSpan TimeToLive { get; }
    public DateTimeOffset TimestampUtc { get; }
}

/// <summary>
/// Event args for snapshot payloads.
/// </summary>
public sealed class CollabSnapshotReceivedEventArgs : EventArgs
{
    public CollabSnapshotReceivedEventArgs(SnapshotMessage snapshot, DateTimeOffset timestampUtc)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        TimestampUtc = timestampUtc;
    }

    public SnapshotMessage Snapshot { get; }
    public DateTimeOffset TimestampUtc { get; }
    public ReadOnlyMemory<byte> Payload => Snapshot.Payload;
}

/// <summary>
/// Event args for protocol errors.
/// </summary>
public sealed class CollabErrorEventArgs : EventArgs
{
    public CollabErrorEventArgs(ErrorMessage error, DateTimeOffset timestampUtc)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
        TimestampUtc = timestampUtc;
    }

    public ErrorMessage Error { get; }
    public DateTimeOffset TimestampUtc { get; }
}

/// <summary>
/// Default realtime session implementation.
/// </summary>
public sealed class CollabRealtimeSession : ICollabRealtimeSession, IAsyncDisposable
{
    private readonly ICollabTransport _transport;
    private readonly CollabRealtimeSessionOptions _options;
    private readonly CollabPresenceThrottler _presenceThrottler;
    private long _sequence;
    private long _lamport;
    private bool _connected;
    private bool _disposed;
    private bool _helloSent;
    private bool _joinSent;

    public CollabRealtimeSession(CollabRealtimeSessionOptions options, ICollabTransport transport)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _presenceThrottler = new CollabPresenceThrottler(_options.PresenceThrottleInterval);
        _transport.MessageReceived += OnMessageReceived;
        _transport.StateChanged += OnTransportStateChanged;
    }

    public Guid DocumentId => _options.DocumentId;
    public Guid SessionId => _options.SessionId;
    public Guid SenderId => _options.SenderId;

    public event EventHandler<CollabTransportStateChangedEventArgs>? TransportStateChanged;
    public event EventHandler<CollabOpsReceivedEventArgs>? OpsReceived;
    public event EventHandler<CollabSnapshotReceivedEventArgs>? SnapshotReceived;
    public event EventHandler<CollabPresenceEventArgs>? PresenceReceived;
    public event EventHandler<CollabErrorEventArgs>? ErrorReceived;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CollabRealtimeSession));
        }

        if (_connected)
        {
            return;
        }

        _helloSent = false;
        _joinSent = false;
        _presenceThrottler.Reset();
        _connected = true;

        if (_transport is ICollabTransportConnection connection)
        {
            await connection.ConnectAsync(cancellationToken);
        }
        else
        {
            TransportStateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Connected));
        }

        await SendHelloAsync(cancellationToken);
        await SendJoinAsync(cancellationToken);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_connected)
        {
            return;
        }

        _connected = false;

        try
        {
            await SendEnvelopeAsync(CollabMessageType.Leave, new LeaveMessage("disconnect"), cancellationToken);
        }
        catch
        {
            // Ignore send failures during disconnect.
        }

        if (_transport is ICollabTransportConnection connection)
        {
            await connection.DisconnectAsync(cancellationToken);
        }
        else
        {
            TransportStateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Disconnected));
        }
    }

    public ValueTask SubmitLocalAsync(CollabOpBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return SendEnvelopeAsync(CollabMessageType.Ops, new OpsMessage(batch), cancellationToken);
    }

    public ValueTask SendPresenceAsync(PresenceState presence, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presence);

        if (!_connected)
        {
            return ValueTask.CompletedTask;
        }

        if (!_presenceThrottler.ShouldSend(DateTimeOffset.UtcNow))
        {
            return ValueTask.CompletedTask;
        }

        var ttl = timeToLive ?? _options.DefaultPresenceTimeToLive;
        if (ttl <= TimeSpan.Zero)
        {
            return ValueTask.CompletedTask;
        }

        return SendEnvelopeAsync(CollabMessageType.Presence, new PresenceMessage(presence, ttl), cancellationToken);
    }

    public ValueTask SendSnapshotAsync(SnapshotMessage snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!_connected)
        {
            return ValueTask.CompletedTask;
        }

        return SendEnvelopeAsync(CollabMessageType.Snapshot, snapshot, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport.MessageReceived -= OnMessageReceived;
        _transport.StateChanged -= OnTransportStateChanged;
        await DisconnectAsync();
    }

    private async ValueTask SendHelloAsync(CancellationToken cancellationToken)
    {
        if (_helloSent)
        {
            return;
        }

        _helloSent = true;
        var payload = new HelloMessage(_options.ClientName, _options.Capabilities, _options.Compression);
        await SendEnvelopeAsync(CollabMessageType.Hello, payload, cancellationToken);
    }

    private async ValueTask SendJoinAsync(CancellationToken cancellationToken)
    {
        if (_joinSent)
        {
            return;
        }

        _joinSent = true;
        var payload = new JoinMessage(_options.DocumentId, _options.KnownVersion, _options.SnapshotId);
        await SendEnvelopeAsync(CollabMessageType.Join, payload, cancellationToken);
    }

    private ValueTask SendEnvelopeAsync<TPayload>(CollabMessageType messageType, TPayload payload, CancellationToken cancellationToken)
    {
        var envelope = new CollabEnvelope<TPayload>(
            CollabProtocolVersion.V1,
            _options.DocumentId,
            _options.SessionId,
            _options.SenderId,
            NextSequence(),
            NextLamport(),
            DateTimeOffset.UtcNow,
            messageType,
            payload);

        var bytes = CollabProtocolJsonCodec.Serialize(envelope);
        var payloadBytes = CollabMessageCompression.MaybeCompress(bytes, _options.Compression, _options.CompressionThresholdBytes);
        return _transport.SendAsync(payloadBytes, cancellationToken);
    }

    private void OnMessageReceived(object? sender, CollabTransportMessageEventArgs e)
    {
        CollabEnvelope<JsonElement> envelope;
        ReadOnlyMemory<byte> payload = e.Payload;
        try
        {
            if (CollabMessageCompression.TryDecompress(payload.Span, _options.MaxDecompressedBytes, out var decompressed, out _))
            {
                payload = decompressed;
            }

            envelope = CollabProtocolJsonCodec.DeserializeEnvelope(payload.Span);
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke(this, new CollabErrorEventArgs(new ErrorMessage("invalid_payload", ex.Message), DateTimeOffset.UtcNow));
            return;
        }

        if (!IsEnvelopeRelevant(envelope))
        {
            return;
        }

        BumpLamport(envelope.Lamport);

        switch (envelope.MessageType)
        {
            case CollabMessageType.Ops:
                HandleOps(envelope);
                break;
            case CollabMessageType.Snapshot:
                HandleSnapshot(envelope);
                break;
            case CollabMessageType.Presence:
                HandlePresence(envelope);
                break;
            case CollabMessageType.Error:
                HandleError(envelope);
                break;
        }
    }

    private void HandleOps(CollabEnvelope<JsonElement> envelope)
    {
        var payload = CollabProtocolJsonCodec.DeserializePayload<OpsMessage>(envelope.Payload);
        OpsReceived?.Invoke(this, new CollabOpsReceivedEventArgs(payload.Batch, envelope.TimestampUtc));
    }

    private void HandleSnapshot(CollabEnvelope<JsonElement> envelope)
    {
        var payload = CollabProtocolJsonCodec.DeserializePayload<SnapshotMessage>(envelope.Payload);
        SnapshotReceived?.Invoke(this, new CollabSnapshotReceivedEventArgs(payload, envelope.TimestampUtc));
    }

    private void HandlePresence(CollabEnvelope<JsonElement> envelope)
    {
        var payload = CollabProtocolJsonCodec.DeserializePayload<PresenceMessage>(envelope.Payload);
        PresenceReceived?.Invoke(this, new CollabPresenceEventArgs(payload.Presence, payload.TimeToLive, envelope.TimestampUtc));
    }

    private void HandleError(CollabEnvelope<JsonElement> envelope)
    {
        var payload = CollabProtocolJsonCodec.DeserializePayload<ErrorMessage>(envelope.Payload);
        ErrorReceived?.Invoke(this, new CollabErrorEventArgs(payload, envelope.TimestampUtc));
    }

    private bool IsEnvelopeRelevant(CollabEnvelope<JsonElement> envelope)
    {
        if (envelope.SenderId == _options.SenderId)
        {
            return false;
        }

        if (_options.DocumentId != Guid.Empty
            && envelope.DocumentId != Guid.Empty
            && envelope.DocumentId != _options.DocumentId)
        {
            return false;
        }

        return true;
    }

    private void OnTransportStateChanged(object? sender, CollabTransportStateChangedEventArgs e)
    {
        if (e.State is CollabTransportState.Disconnected or CollabTransportState.Error)
        {
            _connected = false;
        }
        else if (e.State == CollabTransportState.Connected)
        {
            _connected = true;
        }

        TransportStateChanged?.Invoke(this, e);
    }

    private long NextSequence() => Interlocked.Increment(ref _sequence);

    private long NextLamport() => Interlocked.Increment(ref _lamport);

    private void BumpLamport(long remoteLamport)
    {
        var current = Interlocked.Read(ref _lamport);
        var next = Math.Max(current, remoteLamport) + 1;
        Interlocked.Exchange(ref _lamport, next);
    }

    private sealed class CollabPresenceThrottler
    {
        private readonly TimeSpan _interval;
        private DateTimeOffset _lastSentUtc;

        public CollabPresenceThrottler(TimeSpan interval)
        {
            _interval = interval <= TimeSpan.Zero ? TimeSpan.Zero : interval;
            _lastSentUtc = DateTimeOffset.MinValue;
        }

        public bool ShouldSend(DateTimeOffset now)
        {
            if (_interval == TimeSpan.Zero)
            {
                _lastSentUtc = now;
                return true;
            }

            if (now - _lastSentUtc < _interval)
            {
                return false;
            }

            _lastSentUtc = now;
            return true;
        }

        public void Reset()
        {
            _lastSentUtc = DateTimeOffset.MinValue;
        }
    }
}

using System.IO;
using System.Threading;
using ReactiveUI;
using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Persistence;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.Collaboration.Transports.Local;
using Vibe.Office.Collaboration.Transports.SharedFile;
using Vibe.Office.Collaboration.Transports.WebSocket;

namespace Vibe.Office.Collaboration.UI;

/// <summary>
/// Default implementation of <see cref="ICollabUiService"/> for UI binding.
/// </summary>
public sealed class CollabUiState : ReactiveObject, ICollabUiService, IAsyncDisposable
{
    private static readonly TimeSpan DefaultPresenceTimeToLive = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPresenceThrottle = TimeSpan.FromMilliseconds(80);
    private const string DefaultClientName = "Vibe Office";

    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly SynchronizationContext? _syncContext;
    private readonly Dictionary<Guid, CollabParticipant> _participantMap = new();
    private CollabPresenceRegistry _presenceRegistry;
    private ICollabIdentityService? _identityService;
    private ICollabRealtimeSession? _session;
    private IAsyncDisposable? _transportLifetime;
    private LocalBrokerServer? _hostedBroker;
    private bool _disposed;
    private readonly Timer _presenceTimer;
    private CollabConnectionState _connectionState;
    private string? _connectionMessage;
    private Guid _documentId;
    private Guid _sessionId;
    private CollabTransportMode _transportMode;
    private string? _serverUrl;
    private string? _sharedPath;
    private string? _documentPath;
    private int? _localBrokerPort;
    private Func<byte[]?>? _snapshotSeedProvider;
    private IReadOnlyList<CollabParticipant> _participants;
    private TimeSpan _syncLag;
    private int _opQueueDepth;
    private TimeSpan _snapshotAge;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollabUiState"/> class.
    /// </summary>
    public CollabUiState(ICollabIdentityService? identityService = null)
    {
        _syncContext = SynchronizationContext.Current;
        _presenceRegistry = new CollabPresenceRegistry();
        _participants = Array.Empty<CollabParticipant>();
        _transportMode = CollabTransportMode.LocalBroker;
        _connectionState = CollabConnectionState.Disconnected;
        _documentId = Guid.Empty;
        _sessionId = Guid.Empty;
        _identityService = identityService;
        _presenceTimer = new Timer(_ => PresenceTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        EnsureLocalParticipant();
    }

    /// <inheritdoc />
    public CollabConnectionState ConnectionState
    {
        get => _connectionState;
        private set => this.RaiseAndSetIfChanged(ref _connectionState, value);
    }

    /// <inheritdoc />
    public string? ConnectionMessage
    {
        get => _connectionMessage;
        private set => this.RaiseAndSetIfChanged(ref _connectionMessage, value);
    }

    /// <inheritdoc />
    public Guid DocumentId
    {
        get => _documentId;
        private set
        {
            this.RaiseAndSetIfChanged(ref _documentId, value);
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public Guid SessionId
    {
        get => _sessionId;
        private set
        {
            this.RaiseAndSetIfChanged(ref _sessionId, value);
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public CollabTransportMode TransportMode
    {
        get => _transportMode;
        set
        {
            if (_transportMode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _transportMode, value);
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public string? ServerUrl
    {
        get => _serverUrl;
        set
        {
            if (string.Equals(_serverUrl, value, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _serverUrl, value);
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public string? SharedPath
    {
        get => _sharedPath;
        set
        {
            if (string.Equals(_sharedPath, value, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _sharedPath, value);
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public string? ResolvedSharedPath => ResolveSharedFileBasePath();

    /// <summary>
    /// Updates the current document path for auto-shared sessions.
    /// </summary>
    /// <param name="path">The document path, or <c>null</c> for unsaved documents.</param>
    public void UpdateDocumentPath(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? null : path;
        if (string.Equals(_documentPath, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _documentPath = normalized;
        RaiseStateChanged();
    }

    /// <summary>
    /// Configures a provider used to seed shared-file collaboration sessions.
    /// </summary>
    /// <param name="provider">Provider returning a serialized snapshot payload, or <c>null</c>.</param>
    public void ConfigureSnapshotSeedProvider(Func<byte[]?>? provider)
    {
        _snapshotSeedProvider = provider;
    }

    /// <inheritdoc />
    public int? LocalBrokerPort
    {
        get => _localBrokerPort;
        set
        {
            if (_localBrokerPort == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _localBrokerPort, value);
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CollabParticipant> Participants
    {
        get => _participants;
        private set => this.RaiseAndSetIfChanged(ref _participants, value);
    }

    /// <inheritdoc />
    public IReadOnlyList<PresenceState> Presence => _presenceRegistry.GetActive();

    /// <inheritdoc />
    public TimeSpan SyncLag
    {
        get => _syncLag;
        private set => this.RaiseAndSetIfChanged(ref _syncLag, value);
    }

    /// <inheritdoc />
    public int OpQueueDepth
    {
        get => _opQueueDepth;
        private set => this.RaiseAndSetIfChanged(ref _opQueueDepth, value);
    }

    /// <inheritdoc />
    public TimeSpan SnapshotAge
    {
        get => _snapshotAge;
        private set => this.RaiseAndSetIfChanged(ref _snapshotAge, value);
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <summary>
    /// Raised when the active collaboration session changes.
    /// </summary>
    public event EventHandler? SessionChanged;

    /// <summary>
    /// Gets the active realtime session, if connected.
    /// </summary>
    public ICollabRealtimeSession? ActiveSession => _session;

    /// <inheritdoc />
    public async ValueTask JoinAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            if (ConnectionState is CollabConnectionState.Connecting
                or CollabConnectionState.Connected
                or CollabConnectionState.Reconnecting)
            {
                return;
            }

            SetConnectionState(CollabConnectionState.Connecting);
            SessionId = Guid.NewGuid();
            if (DocumentId == Guid.Empty)
            {
                DocumentId = Guid.NewGuid();
            }

            EnsureLocalParticipant();
            await StartSessionAsync(cancellationToken);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask LeaveAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            await StopSessionAsync(cancellationToken);
            ResetPresence();
            SetConnectionState(CollabConnectionState.Disconnected);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask ShareAsync(CancellationToken cancellationToken = default)
    {
        var message = TransportMode switch
        {
            CollabTransportMode.SharedFile => "Shared file path ready.",
            CollabTransportMode.SharedFileAuto => "Shared session path ready.",
            CollabTransportMode.Server => "Share link copied.",
            _ => "Local broker ready."
        };
        SetConnectionState(ConnectionState, message);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            SetConnectionState(CollabConnectionState.Reconnecting);
            await StopSessionAsync(cancellationToken);
            ResetPresence();
            await StartSessionAsync(cancellationToken);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <inheritdoc />
    public void UpdatePresence(PresenceState presence, TimeSpan? timeToLive = null)
    {
        UpdatePresenceInternal(presence, timeToLive, ShouldBroadcast(presence));
    }

    /// <inheritdoc />
    public void UpdateParticipants(IReadOnlyList<CollabParticipant> participants)
    {
        _participantMap.Clear();
        foreach (var participant in participants)
        {
            var normalized = NormalizeParticipant(participant);
            _participantMap[normalized.UserId] = normalized;
        }

        EnsureLocalParticipant(refresh: false);
        RefreshParticipants();
        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void UpdateDiagnostics(TimeSpan syncLag, int opQueueDepth, TimeSpan snapshotAge)
    {
        SyncLag = syncLag;
        OpQueueDepth = opQueueDepth;
        SnapshotAge = snapshotAge;
        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void SetConnectionState(CollabConnectionState state, string? message = null)
    {
        ConnectionState = state;
        if (message is not null)
        {
            ConnectionMessage = message;
        }
        else if (state != CollabConnectionState.Error)
        {
            ConnectionMessage = null;
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void ClearError()
    {
        ConnectionMessage = null;
        if (ConnectionState == CollabConnectionState.Error)
        {
            ConnectionState = CollabConnectionState.Disconnected;
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Disposes the collaboration session and transport resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _presenceTimer.Dispose();
        await StopSessionAsync(CancellationToken.None);
        _sessionGate.Dispose();
    }

    private async ValueTask StartSessionAsync(CancellationToken cancellationToken)
    {
        await StopSessionAsync(cancellationToken);

        var userId = ResolveLocalUserId();
        if (userId == Guid.Empty)
        {
            SetConnectionState(CollabConnectionState.Error, "Collaboration identity is not configured.");
            return;
        }

        try
        {
            if (TransportMode == CollabTransportMode.LocalBroker)
            {
                await StartLocalBrokerSessionAsync(userId, cancellationToken);
                return;
            }

            if (TransportMode is CollabTransportMode.SharedFile or CollabTransportMode.SharedFileAuto)
            {
                await TrySeedSharedFileSnapshotAsync(cancellationToken);
                var session = CreateSharedFileSession(userId);
                AttachSession(session);
                _session = session;
                RaiseSessionChanged();
                await session.ConnectAsync(cancellationToken);
                return;
            }

            var transport = CreateTransport();
            var realtimeSession = CreateSession(userId, transport);
            _transportLifetime = transport as IAsyncDisposable;
            AttachSession(realtimeSession);
            _session = realtimeSession;
            RaiseSessionChanged();
            await realtimeSession.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await StopSessionAsync(cancellationToken);
            SetConnectionState(CollabConnectionState.Error, ex.Message);
        }
    }

    private async ValueTask StartLocalBrokerSessionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var port = NormalizeLocalBrokerPort();
        if (port <= 0)
        {
            await EnsureLocalBrokerAsync(0);
            port = LocalBrokerPort ?? 0;
        }

        var transport = new LocalBrokerClientTransport("127.0.0.1", port);
        var session = CreateSession(userId, transport);
        _transportLifetime = transport;
        AttachSession(session);
        _session = session;
        RaiseSessionChanged();

        try
        {
            await session.ConnectAsync(cancellationToken);
        }
        catch
        {
            await StopSessionAsync(cancellationToken);
            await EnsureLocalBrokerAsync(port);

            transport = new LocalBrokerClientTransport("127.0.0.1", LocalBrokerPort ?? port);
            session = CreateSession(userId, transport);
            _transportLifetime = transport;
            AttachSession(session);
            _session = session;
            RaiseSessionChanged();
            await session.ConnectAsync(cancellationToken);
        }
    }

    private async ValueTask EnsureLocalBrokerAsync(int port)
    {
        if (_hostedBroker is not null)
        {
            return;
        }

        var server = new LocalBrokerServer(port);
        server.Start();
        _hostedBroker = server;
        if (LocalBrokerPort != server.Port)
        {
            LocalBrokerPort = server.Port;
        }

        await Task.CompletedTask;
    }

    private async ValueTask StopSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            DetachSession(_session);
            try
            {
                await _session.DisconnectAsync(cancellationToken);
            }
            catch
            {
                // Ignore shutdown failures.
            }
        }

        if (_session is IAsyncDisposable sessionDisposable)
        {
            await sessionDisposable.DisposeAsync();
        }

        _session = null;
        RaiseSessionChanged();

        if (_transportLifetime is not null)
        {
            await _transportLifetime.DisposeAsync();
            _transportLifetime = null;
        }

        if (_hostedBroker is not null)
        {
            await _hostedBroker.DisposeAsync();
            _hostedBroker = null;
        }
    }

    private ICollabTransport CreateTransport()
    {
        return TransportMode switch
        {
            CollabTransportMode.Server => CreateServerTransport(),
            _ => new LocalBrokerClientTransport("127.0.0.1", NormalizeLocalBrokerPort())
        };
    }

    private string? ResolveSharedFileBasePath()
    {
        if (TransportMode == CollabTransportMode.SharedFile)
        {
            if (string.IsNullOrWhiteSpace(SharedPath))
            {
                return null;
            }

            return CollabPersistedFormat.NormalizeBasePath(SharedPath.Trim());
        }

        if (TransportMode != CollabTransportMode.SharedFileAuto)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(SharedPath))
        {
            return CollabPersistedFormat.NormalizeBasePath(SharedPath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_documentPath))
        {
            return CollabPersistedFormat.NormalizeBasePath(_documentPath);
        }

        if (SessionId == Guid.Empty)
        {
            return null;
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VibeOffice",
            "CollabSessions");
        return Path.Combine(root, SessionId.ToString("N"));
    }

    private async ValueTask TrySeedSharedFileSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_snapshotSeedProvider is null)
        {
            return;
        }

        var basePath = ResolveSharedFileBasePath();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return;
        }

        var snapshotPath = basePath + CollabPersistedFormat.SnapshotExtension;
        var opLogPath = basePath + CollabPersistedFormat.OpLogExtension;

        if (HasData(snapshotPath) || HasData(opLogPath))
        {
            return;
        }

        var payload = _snapshotSeedProvider();
        if (payload is null || payload.Length == 0)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var serializer = new CollabSnapshotSerializer();
            var snapshot = serializer.DeserializeSnapshot(payload);
            var store = new FileCollabSnapshotStore(basePath);
            await store.WriteSnapshotAsync(snapshot, cancellationToken);
        }
        catch (IOException)
        {
            // Another session seeded the snapshot first.
        }
        catch (InvalidDataException)
        {
            // Ignore invalid seed payloads.
        }
    }

    private static bool HasData(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return new FileInfo(path).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private ICollabRealtimeSession CreateSharedFileSession(Guid userId)
    {
        var basePath = ResolveSharedFileBasePath();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException("Shared file path is required for collaboration.");
        }

        var directory = Path.GetDirectoryName(basePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var options = new CollabRealtimeSessionOptions
        {
            DocumentId = DocumentId,
            SessionId = SessionId,
            SenderId = userId,
            ClientName = DefaultClientName,
            DefaultPresenceTimeToLive = DefaultPresenceTimeToLive,
            PresenceThrottleInterval = DefaultPresenceThrottle
        };

        var transportOptions = new SharedFileTransportOptions(
            PollInterval: TimeSpan.FromMilliseconds(100),
            WriteLockTimeout: TimeSpan.FromSeconds(5));

        var snapshotOptions = new CollabSnapshotStoreOptions(
            OpCountThreshold: 5000,
            LogSizeThresholdBytes: 8 * 1024 * 1024,
            SnapshotInterval: TimeSpan.FromMinutes(2),
            TailOpCount: 128);

        return new SharedFileCollabSession(basePath, options, transportOptions, snapshotOptions);
    }

    private ICollabTransport CreateServerTransport()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            throw new InvalidOperationException("Server URL is required for collaboration.");
        }

        if (!Uri.TryCreate(ServerUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Server URL is invalid.");
        }

        return new WebSocketClientTransport(uri);
    }

    private int NormalizeLocalBrokerPort()
    {
        if (!LocalBrokerPort.HasValue)
        {
            return 0;
        }

        return LocalBrokerPort.Value;
    }

    private CollabRealtimeSession CreateSession(Guid userId, ICollabTransport transport)
    {
        var options = new CollabRealtimeSessionOptions
        {
            DocumentId = DocumentId,
            SessionId = SessionId,
            SenderId = userId,
            ClientName = DefaultClientName,
            DefaultPresenceTimeToLive = DefaultPresenceTimeToLive,
            PresenceThrottleInterval = DefaultPresenceThrottle
        };

        return new CollabRealtimeSession(options, transport);
    }

    private void AttachSession(ICollabRealtimeSession session)
    {
        session.TransportStateChanged += OnTransportStateChanged;
        session.PresenceReceived += OnPresenceReceived;
        session.ErrorReceived += OnErrorReceived;
        session.OpsReceived += OnOpsReceived;
    }

    private void DetachSession(ICollabRealtimeSession session)
    {
        session.TransportStateChanged -= OnTransportStateChanged;
        session.PresenceReceived -= OnPresenceReceived;
        session.ErrorReceived -= OnErrorReceived;
        session.OpsReceived -= OnOpsReceived;
    }

    private void OnTransportStateChanged(object? sender, CollabTransportStateChangedEventArgs e)
    {
        Dispatch(() =>
        {
            var state = e.State switch
            {
                CollabTransportState.Connecting => CollabConnectionState.Connecting,
                CollabTransportState.Connected => CollabConnectionState.Connected,
                CollabTransportState.Error => CollabConnectionState.Error,
                _ => CollabConnectionState.Disconnected
            };

            SetConnectionState(state, e.Message);
        });
    }

    private void OnPresenceReceived(object? sender, CollabPresenceEventArgs e)
    {
        Dispatch(() =>
        {
            UpdateSyncLag(e.TimestampUtc);
            UpdatePresenceInternal(e.Presence, e.TimeToLive, broadcast: false);
        });
    }

    private void OnOpsReceived(object? sender, CollabOpsReceivedEventArgs e)
    {
        Dispatch(() => UpdateSyncLag(e.TimestampUtc));
    }

    private void OnErrorReceived(object? sender, CollabErrorEventArgs e)
    {
        Dispatch(() =>
        {
            SetConnectionState(CollabConnectionState.Error, e.Error.Message);
        });
    }

    private void UpdatePresenceInternal(PresenceState presence, TimeSpan? timeToLive, bool broadcast)
    {
        _presenceRegistry.Update(presence, timeToLive);
        MergeParticipantFromPresence(presence);

        if (broadcast && _session is not null)
        {
            _ = _session.SendPresenceAsync(presence, timeToLive);
        }

        RaiseStateChanged();
    }

    private bool ShouldBroadcast(PresenceState presence)
    {
        return _session is not null && presence.UserId == _session.SenderId;
    }

    private void MergeParticipantFromPresence(PresenceState presence)
    {
        var isLocal = _identityService is not null && presence.UserId == _identityService.UserId;
        var color = CollabColorPalette.ResolveColor(presence.Color, presence.UserId);
        var displayName = isLocal && _identityService is not null
            ? _identityService.DisplayName
            : presence.DisplayName;
        var participant = new CollabParticipant(presence.UserId, displayName, color, presence.UpdatedAtUtc, isLocal);
        _participantMap[presence.UserId] = participant;
        RefreshParticipants();
    }

    private void EnsureLocalParticipant(bool refresh = true)
    {
        if (_identityService is null)
        {
            return;
        }

        var participant = new CollabParticipant(
            _identityService.UserId,
            _identityService.DisplayName,
            _identityService.Color,
            DateTimeOffset.UtcNow,
            IsLocal: true);

        _participantMap[participant.UserId] = NormalizeParticipant(participant);
        if (refresh)
        {
            RefreshParticipants();
            RaiseStateChanged();
        }
    }

    private static CollabParticipant NormalizeParticipant(CollabParticipant participant)
    {
        return participant with
        {
            Color = CollabColorPalette.ResolveColor(participant.Color, participant.UserId)
        };
    }

    private Guid ResolveLocalUserId()
    {
        if (_identityService is not null)
        {
            return _identityService.UserId;
        }

        return Guid.Empty;
    }

    private void RefreshParticipants()
    {
        Participants = _participantMap.Values
            .OrderByDescending(participant => participant.IsLocal)
            .ThenBy(participant => participant.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ResetPresence()
    {
        _presenceRegistry = new CollabPresenceRegistry();
        foreach (var participant in _participantMap.Values.Where(p => !p.IsLocal).ToArray())
        {
            _participantMap.Remove(participant.UserId);
        }

        foreach (var participant in _participantMap.Values.Where(p => p.IsLocal).ToArray())
        {
            _participantMap[participant.UserId] = participant with { LastActiveUtc = DateTimeOffset.UtcNow };
        }

        RefreshParticipants();
        RaiseStateChanged();
    }

    private void UpdateSyncLag(DateTimeOffset timestampUtc)
    {
        var lag = DateTimeOffset.UtcNow - timestampUtc;
        if (lag < TimeSpan.Zero)
        {
            lag = TimeSpan.Zero;
        }

        SyncLag = lag;
    }

    private void Dispatch(Action action)
    {
        if (_syncContext is null || SynchronizationContext.Current == _syncContext)
        {
            action();
            return;
        }

        _syncContext.Post(_ => action(), null);
    }

    private void PresenceTick()
    {
        if (_disposed)
        {
            return;
        }

        if (ConnectionState is CollabConnectionState.Disconnected or CollabConnectionState.Offline)
        {
            return;
        }

        Dispatch(RaiseStateChanged);
    }

    private void RaiseSessionChanged()
    {
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using Vibe.Office.Collaboration.Persistence;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.Documents;

namespace Vibe.Office.Collaboration.Transports.SharedFile;

public sealed class SharedFileCollabSession : ICollabRealtimeSession, IAsyncDisposable
{
    private readonly string _basePath;
    private readonly string _opLogPath;
    private readonly string _presencePath;
    private readonly string _snapshotIndexPath;
    private readonly CollabRealtimeSessionOptions _options;
    private readonly FileCollabSnapshotStore _snapshotStore;
    private readonly CollabSnapshotSerializer _snapshotSerializer = new();
    private readonly SharedFileTransport _opTransport;
    private readonly SharedFileTransport _presenceTransport;
    private readonly SharedFileTransportOptions _transportOptions;
    private DateTimeOffset _lastPresenceSentUtc;
    private DateTimeOffset _lastCompactionCheckUtc;
    private readonly SemaphoreSlim _compactGate = new(1, 1);
    private readonly Channel<CollabOpBatch> _outbox;
    private CancellationTokenSource? _outboxCts;
    private Task? _outboxTask;
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private CancellationTokenSource? _snapshotCts;
    private Task? _snapshotPollTask;
    private FileSystemWatcher? _snapshotWatcher;
    private long _latestSnapshotVersion;
    private bool _connected;
    private bool _disposed;
    private const int MaxPresenceLogBytes = 256 * 1024;
    private static readonly TimeSpan CompactionCheckInterval = TimeSpan.FromSeconds(30);

    public SharedFileCollabSession(
        string basePath,
        CollabRealtimeSessionOptions options,
        SharedFileTransportOptions? transportOptions = null,
        CollabSnapshotStoreOptions? snapshotOptions = null)
    {
        ArgumentNullException.ThrowIfNull(basePath);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _basePath = CollabPersistedFormat.NormalizeBasePath(basePath);
        _opLogPath = _basePath + CollabPersistedFormat.OpLogExtension;
        _presencePath = _basePath + CollabPersistedFormat.PresenceExtension;
        _snapshotIndexPath = _basePath + CollabPersistedFormat.SnapshotIndexExtension;
        _snapshotStore = new FileCollabSnapshotStore(_basePath, snapshotOptions);
        _transportOptions = transportOptions ?? SharedFileTransportOptions.Default;
        _opTransport = new SharedFileTransport(_opLogPath, _transportOptions);
        _presenceTransport = new SharedFileTransport(_presencePath, _transportOptions);
        _outbox = Channel.CreateUnbounded<CollabOpBatch>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _opTransport.MessageReceived += OnOpMessageReceived;
        _opTransport.StateChanged += OnTransportStateChanged;
        _presenceTransport.MessageReceived += OnPresenceMessageReceived;
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
            throw new ObjectDisposedException(nameof(SharedFileCollabSession));
        }

        if (_connected)
        {
            return;
        }

        _connected = true;
        _lastPresenceSentUtc = DateTimeOffset.MinValue;

        try
        {
            await InitializeAsync(cancellationToken);
            EnsureOutbox();
            EnsureSnapshotWatcher();
            await _opTransport.ConnectAsync(cancellationToken);
            await _presenceTransport.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            TransportStateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Error, ex.Message));
            throw;
        }
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_connected)
        {
            return;
        }

        _connected = false;
        await _opTransport.DisconnectAsync(cancellationToken);
        await _presenceTransport.DisconnectAsync(cancellationToken);
        await StopSnapshotWatcherAsync(cancellationToken);
        await StopOutboxAsync(cancellationToken);
    }

    public async ValueTask SubmitLocalAsync(CollabOpBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureOutbox();
        if (!_outbox.Writer.TryWrite(batch))
        {
            await _outbox.Writer.WriteAsync(batch, cancellationToken);
        }
    }

    public async ValueTask SendPresenceAsync(
        PresenceState presence,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presence);
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var throttle = _options.PresenceThrottleInterval;
        if (throttle > TimeSpan.Zero && now - _lastPresenceSentUtc < throttle)
        {
            return;
        }

        _lastPresenceSentUtc = now;
        var ttl = timeToLive ?? _options.DefaultPresenceTimeToLive;
        var payload = CollabProtocolJsonCodec.SerializePayload(new PresenceMessage(presence, ttl));
        await _presenceTransport.SendAsync(payload, cancellationToken);
        TrimPresenceLogIfNeeded();
    }

    public async ValueTask SendSnapshotAsync(SnapshotMessage snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var collabSnapshot = _snapshotSerializer.DeserializeSnapshot(snapshot.Payload.AsSpan());
        await _snapshotStore.WriteSnapshotAsync(collabSnapshot, cancellationToken);
        _latestSnapshotVersion = Math.Max(_latestSnapshotVersion, collabSnapshot.Version);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectAsync();
        _opTransport.MessageReceived -= OnOpMessageReceived;
        _opTransport.StateChanged -= OnTransportStateChanged;
        _presenceTransport.MessageReceived -= OnPresenceMessageReceived;
        await _opTransport.DisposeAsync();
        await _presenceTransport.DisposeAsync();
        _snapshotGate.Dispose();
    }

    private void EnsureOutbox()
    {
        if (_outboxTask is not null)
        {
            return;
        }

        _outboxCts = new CancellationTokenSource();
        _outboxTask = Task.Run(() => ProcessOutboxAsync(_outbox.Reader, _outboxCts.Token));
    }

    private async Task StopOutboxAsync(CancellationToken cancellationToken)
    {
        if (_outboxTask is null)
        {
            return;
        }

        _outbox.Writer.TryComplete();
        try
        {
            await _outboxTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _outboxCts?.Cancel();
        }
        catch
        {
        }
        finally
        {
            _outboxCts?.Dispose();
            _outboxCts = null;
            _outboxTask = null;
        }
    }

    private async Task ProcessOutboxAsync(ChannelReader<CollabOpBatch> reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                var batches = new List<CollabOpBatch>();
                while (reader.TryRead(out var batch))
                {
                    batches.Add(batch);
                }

                if (batches.Count == 0)
                {
                    continue;
                }

                await _snapshotStore.AppendOpsAsync(batches, cancellationToken);
                await CompactIfNeededAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke(this, new CollabErrorEventArgs(new ErrorMessage("shared-file", ex.Message), DateTimeOffset.UtcNow));
        }
    }

    private async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        var recovery = await RecoverAsync(cancellationToken);
        var snapshot = CollabSnapshot.Create(recovery.Version, recovery.Document);
        _latestSnapshotVersion = snapshot.Version;
        var payload = _snapshotSerializer.Serialize(snapshot);
        SnapshotReceived?.Invoke(this, new CollabSnapshotReceivedEventArgs(
            new SnapshotMessage(snapshot.SnapshotId, snapshot.Version, payload),
            DateTimeOffset.UtcNow));

        var opLogLength = File.Exists(_opLogPath) ? new FileInfo(_opLogPath).Length : 0;
        _opTransport.SetReadPosition(opLogLength);

        var presenceLength = File.Exists(_presencePath) ? new FileInfo(_presencePath).Length : 0;
        _presenceTransport.SetReadPosition(presenceLength);
    }

    private void EnsureSnapshotWatcher()
    {
        if (_snapshotPollTask is not null || _snapshotWatcher is not null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_snapshotIndexPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _snapshotWatcher = new FileSystemWatcher(directory ?? string.Empty, Path.GetFileName(_snapshotIndexPath))
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _snapshotWatcher.Changed += (_, _) => _ = CheckForSnapshotAsync(CancellationToken.None);
        _snapshotWatcher.Created += (_, _) => _ = CheckForSnapshotAsync(CancellationToken.None);
        _snapshotWatcher.Renamed += (_, _) => _ = CheckForSnapshotAsync(CancellationToken.None);
        _snapshotWatcher.EnableRaisingEvents = true;

        _snapshotCts = new CancellationTokenSource();
        _snapshotPollTask = Task.Run(() => SnapshotPollLoopAsync(_snapshotCts.Token));
    }

    private async Task StopSnapshotWatcherAsync(CancellationToken cancellationToken)
    {
        if (_snapshotPollTask is null && _snapshotWatcher is null)
        {
            return;
        }

        _snapshotCts?.Cancel();
        if (_snapshotPollTask is not null)
        {
            try
            {
                await _snapshotPollTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _snapshotCts?.Dispose();
        _snapshotCts = null;
        _snapshotPollTask = null;
        _snapshotWatcher?.Dispose();
        _snapshotWatcher = null;
    }

    private async Task SnapshotPollLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_transportOptions.PollInterval, cancellationToken);
                await CheckForSnapshotAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckForSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_disposed || !_connected)
        {
            return;
        }

        if (!await _snapshotGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (!_snapshotStore.TryReadSnapshotIndex(out var index))
            {
                return;
            }

            if (index.Version <= _latestSnapshotVersion)
            {
                return;
            }

            var snapshot = await _snapshotStore.LoadLatestSnapshotAsync(cancellationToken);
            if (snapshot is null || snapshot.Version <= _latestSnapshotVersion)
            {
                return;
            }

            _latestSnapshotVersion = snapshot.Version;

            var payload = _snapshotSerializer.Serialize(snapshot);
            SnapshotReceived?.Invoke(this, new CollabSnapshotReceivedEventArgs(
                new SnapshotMessage(snapshot.SnapshotId, snapshot.Version, payload),
                DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            ErrorReceived?.Invoke(this, new CollabErrorEventArgs(new ErrorMessage("shared-file", ex.Message), DateTimeOffset.UtcNow));
        }
        finally
        {
            _snapshotGate.Release();
        }
    }

    private async ValueTask<CollabRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _snapshotStore.LoadLatestSnapshotAsync(cancellationToken);
        var document = snapshot?.Document ?? new Document();
        var version = snapshot?.Version ?? 0;

        var engine = new InMemoryCollabEngine(document, version);
        if (File.Exists(_opLogPath))
        {
            var startOffset = ResolveOpLogStartOffset(snapshot);
            using var reader = new CollabOpLogReader(_opLogPath, startOffset);
            foreach (var batch in reader.ReadAll())
            {
                if (!TrySliceBatch(batch, version, out var sliced))
                {
                    continue;
                }

                engine.Apply(sliced, CollabApplyOrigin.Remote);
                _snapshotStore.ObserveRemoteOps(sliced.Ops.Count);
            }
        }

        return new CollabRecoveryResult(engine.Document, engine.Version, snapshot?.SnapshotId);
    }

    private long ResolveOpLogStartOffset(CollabSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return 0;
        }

        if (_snapshotStore.TryReadSnapshotIndex(out var index)
            && index.Version == snapshot.Version)
        {
            if (File.Exists(_opLogPath))
            {
                var length = new FileInfo(_opLogPath).Length;
                if (index.OpLogLength <= length)
                {
                    return index.OpLogLength;
                }
            }
        }

        return 0;
    }

    private static bool TrySliceBatch(CollabOpBatch batch, long snapshotVersion, out CollabOpBatch sliced)
    {
        if (batch.Ops.Count == 0)
        {
            sliced = batch;
            return false;
        }

        var batchEnd = batch.BaseVersion + batch.Ops.Count;
        if (batchEnd <= snapshotVersion)
        {
            sliced = batch;
            return false;
        }

        if (batch.BaseVersion >= snapshotVersion)
        {
            sliced = batch;
            return true;
        }

        var skip = (int)Math.Clamp(snapshotVersion - batch.BaseVersion, 0, batch.Ops.Count);
        if (skip >= batch.Ops.Count)
        {
            sliced = batch;
            return false;
        }

        var remaining = batch.Ops.Skip(skip).ToArray();
        sliced = batch with { BaseVersion = snapshotVersion, Ops = remaining };
        return true;
    }

    private void OnOpMessageReceived(object? sender, CollabTransportMessageEventArgs e)
    {
        try
        {
            var batch = CollabOpBatchJsonCodec.Deserialize(e.Payload.Span);
            if (batch.ActorId == SenderId)
            {
                return;
            }

            _snapshotStore.ObserveRemoteOps(batch.Ops.Count);
            OpsReceived?.Invoke(this, new CollabOpsReceivedEventArgs(batch, batch.TimestampUtc));
            _ = CompactIfNeededAsync();
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke(this, new CollabErrorEventArgs(new ErrorMessage("shared-file", ex.Message), DateTimeOffset.UtcNow));
        }
    }

    private void OnPresenceMessageReceived(object? sender, CollabTransportMessageEventArgs e)
    {
        try
        {
            var message = CollabProtocolJsonCodec.DeserializePayload<PresenceMessage>(e.Payload.Span);
            PresenceReceived?.Invoke(this, new CollabPresenceEventArgs(message.Presence, message.TimeToLive, message.Presence.UpdatedAtUtc));
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke(this, new CollabErrorEventArgs(new ErrorMessage("shared-file", ex.Message), DateTimeOffset.UtcNow));
        }
    }

    private void OnTransportStateChanged(object? sender, CollabTransportStateChangedEventArgs e)
    {
        TransportStateChanged?.Invoke(this, e);
    }

    private async Task CompactIfNeededAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastCompactionCheckUtc < CompactionCheckInterval)
        {
            return;
        }

        if (!await _compactGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            _lastCompactionCheckUtc = now;
            await _snapshotStore.CompactAsync();
        }
        catch
        {
            // Ignore background compaction failures.
        }
        finally
        {
            _compactGate.Release();
        }
    }

    private void TrimPresenceLogIfNeeded()
    {
        try
        {
            var info = new FileInfo(_presencePath);
            if (!info.Exists || info.Length <= MaxPresenceLogBytes)
            {
                return;
            }

            var lockPath = _presencePath + ".lock";
            using var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var stream = new FileStream(_presencePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            stream.SetLength(0);
        }
        catch
        {
            // Ignore trimming failures.
        }
    }
}

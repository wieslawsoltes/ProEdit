using ProEdit.Collaboration;
using ProEdit.Collaboration.Persistence;

namespace ProEdit.Collaboration.Transports.SharedFile;

public sealed class SharedFileTransport : ICollabTransportConnection, IAsyncDisposable
{
    private readonly string _path;
    private readonly SharedFileTransportOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private long _readPosition;
    private FileSystemWatcher? _watcher;
    private Task? _pollTask;
    private bool _started;
    private bool _disposed;

    public SharedFileTransport(string path, SharedFileTransportOptions? options = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _options = options ?? SharedFileTransportOptions.Default;
        EnsureFileExists();
    }

    public static SharedFileTransport CreateForBasePath(string basePath, SharedFileTransportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(basePath);
        var path = basePath + CollabPersistedFormat.OpLogExtension;
        return new SharedFileTransport(path, options);
    }

    public event EventHandler<CollabTransportMessageEventArgs>? MessageReceived;
    public event EventHandler<CollabTransportStateChangedEventArgs>? StateChanged;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_path) ?? string.Empty, Path.GetFileName(_path))
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite
        };
        _watcher.Changed += (_, _) => _ = ReadAvailableAsync();

        _pollTask = Task.Run(PollLoopAsync);
        StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Connected));
        _started = true;
    }

    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _cts.Cancel();
        if (_pollTask is not null)
        {
            await _pollTask;
        }

        _watcher?.Dispose();
        _watcher = null;
        _pollTask = null;
        _started = false;
        StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Disconnected));
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        AppendRecord(payload.Span);
        return ValueTask.CompletedTask;
    }

    public void SetReadPosition(long position)
    {
        if (position < 0)
        {
            position = 0;
        }

        if (_started)
        {
            _readGate.Wait();
            try
            {
                _readPosition = position;
            }
            finally
            {
                _readGate.Release();
            }
        }
        else
        {
            _readPosition = position;
        }
    }

    private void AppendRecord(ReadOnlySpan<byte> payload)
    {
        var checksum = CollabChecksum.Compute(payload);
        var lockPath = _path + ".lock";
        using var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(CollabPersistedFormat.OpLogMagic);
        writer.Write(CollabPersistedFormat.OpLogVersion);
        writer.Write(payload.Length);
        writer.Write(checksum);
        writer.Write(payload);
        writer.Flush();
    }

    private async Task PollLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(_options.PollInterval, _cts.Token);
                await ReadAvailableAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReadAvailableAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _readGate.WaitAsync();
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (_readPosition > stream.Length)
            {
                _readPosition = 0;
            }

            stream.Seek(_readPosition, SeekOrigin.Begin);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            while (stream.Position < stream.Length)
            {
                var magic = reader.ReadUInt32();
                if (magic != CollabPersistedFormat.OpLogMagic)
                {
                    break;
                }

                var version = reader.ReadInt32();
                if (version != CollabPersistedFormat.OpLogVersion)
                {
                    break;
                }

                var length = reader.ReadInt32();
                var checksum = reader.ReadBytes(4);
                var payload = reader.ReadBytes(length);

                if (payload.Length < length)
                {
                    break;
                }

                var actualChecksum = CollabChecksum.Compute(payload);
                if (!checksum.SequenceEqual(actualChecksum))
                {
                    break;
                }

                MessageReceived?.Invoke(this, new CollabTransportMessageEventArgs(payload));
                _readPosition = stream.Position;
            }
        }
        catch (IOException)
        {
        }
        catch (Exception ex)
        {
            StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Error, ex.Message));
        }
        finally
        {
            _readGate.Release();
        }

        await Task.CompletedTask;
    }

    private void EnsureFileExists()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_path))
        {
            using var stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync();
        _disposed = true;
        _cts.Dispose();
        _readGate.Dispose();
    }
}

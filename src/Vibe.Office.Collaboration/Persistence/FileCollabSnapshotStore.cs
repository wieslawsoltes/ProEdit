using System.Linq;
using Vibe.Office.Documents;

namespace Vibe.Office.Collaboration.Persistence;

public sealed class FileCollabSnapshotStore : ICollabSnapshotStore
{
    private readonly string _snapshotPath;
    private readonly string _snapshotIndexPath;
    private readonly string _opLogPath;
    private readonly CollabSnapshotSerializer _serializer;
    private readonly CollabSnapshotIndexSerializer _indexSerializer;
    private readonly CollabSnapshotStoreOptions _options;
    private readonly object _sync = new();
    private DateTimeOffset _lastSnapshotTime;
    private int _opCountSinceSnapshot;

    public FileCollabSnapshotStore(string basePath, CollabSnapshotStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(basePath);
        _snapshotPath = basePath + CollabPersistedFormat.SnapshotExtension;
        _snapshotIndexPath = basePath + CollabPersistedFormat.SnapshotIndexExtension;
        _opLogPath = basePath + CollabPersistedFormat.OpLogExtension;
        _serializer = new CollabSnapshotSerializer();
        _indexSerializer = new CollabSnapshotIndexSerializer();
        _options = options ?? new CollabSnapshotStoreOptions();
    }

    public ValueTask<CollabSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return ValueTask.FromResult<CollabSnapshot?>(null);
        }

        var payload = File.ReadAllBytes(_snapshotPath);
        var snapshot = _serializer.DeserializeSnapshot(payload);
        _lastSnapshotTime = File.GetLastWriteTimeUtc(_snapshotPath);
        _opCountSinceSnapshot = 0;
        return ValueTask.FromResult<CollabSnapshot?>(snapshot);
    }

    public ValueTask AppendOpsAsync(CollabOpBatch batch, CancellationToken cancellationToken = default)
    {
        return AppendOpsAsync(new[] { batch }, cancellationToken);
    }

    public ValueTask AppendOpsAsync(IReadOnlyList<CollabOpBatch> batches, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (batches is null || batches.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        lock (_sync)
        {
            using var fileLock = AcquireLock();
            using var writer = new CollabOpLogWriter(_opLogPath);
            writer.AppendMany(batches);
            foreach (var batch in batches)
            {
                _opCountSinceSnapshot += batch.Ops.Count;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask WriteSnapshotAsync(CollabSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = _serializer.Serialize(snapshot);
        lock (_sync)
        {
            var tempPath = _snapshotPath + ".tmp";
            File.WriteAllBytes(tempPath, payload);
            File.Copy(tempPath, _snapshotPath, overwrite: true);
            File.Delete(tempPath);
            _lastSnapshotTime = DateTimeOffset.UtcNow;
            _opCountSinceSnapshot = 0;
            WriteSnapshotIndex(snapshot.Version);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CompactAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_opLogPath))
        {
            return ValueTask.CompletedTask;
        }

        var info = new FileInfo(_opLogPath);
        if (!ShouldCompact(info))
        {
            return ValueTask.CompletedTask;
        }

        lock (_sync)
        {
            using var fileLock = AcquireLock();
            CollabSnapshot? existingSnapshot = null;
            if (File.Exists(_snapshotPath))
            {
                existingSnapshot = _serializer.DeserializeSnapshot(File.ReadAllBytes(_snapshotPath));
            }

            var baseDocument = existingSnapshot?.Document ?? new Document();
            var baseSnapshotVersion = existingSnapshot?.Version ?? 0;

            var engine = new InMemoryCollabEngine(baseDocument, baseSnapshotVersion);
            var tail = new Queue<CollabOpBatch>(Math.Max(0, _options.TailOpCount));

            // Reader must be disposed before rewriting/deleting the op-log file on Windows.
            using (var reader = new CollabOpLogReader(_opLogPath))
            {
                foreach (var batch in reader.ReadAll())
                {
                    if (TrySliceBatch(batch, baseSnapshotVersion, out var sliced))
                    {
                        engine.Apply(sliced, CollabApplyOrigin.Remote);
                    }

                    EnqueueTail(tail, batch);
                }
            }

            var snapshot = CollabSnapshot.Create(engine.Version, engine.Document);
            var payload = _serializer.Serialize(snapshot);
            File.WriteAllBytes(_snapshotPath, payload);

            RewriteOpLog(tail);
            _lastSnapshotTime = DateTimeOffset.UtcNow;
            _opCountSinceSnapshot = tail.Sum(entry => entry.Ops.Count);
            WriteSnapshotIndex(snapshot.Version);
        }

        return ValueTask.CompletedTask;
    }

    public bool TryReadSnapshotIndex(out CollabSnapshotIndex index)
    {
        if (!File.Exists(_snapshotIndexPath))
        {
            index = default;
            return false;
        }

        try
        {
            var payload = File.ReadAllBytes(_snapshotIndexPath);
            index = _indexSerializer.Deserialize(payload);
            return true;
        }
        catch
        {
            index = default;
            return false;
        }
    }

    public void ObserveRemoteOps(int opCount)
    {
        if (opCount <= 0)
        {
            return;
        }

        lock (_sync)
        {
            _opCountSinceSnapshot += opCount;
        }
    }

    private bool ShouldCompact(FileInfo logInfo)
    {
        if (_opCountSinceSnapshot >= _options.OpCountThreshold)
        {
            return true;
        }

        if (logInfo.Length >= _options.LogSizeThresholdBytes)
        {
            return true;
        }

        if (_lastSnapshotTime == default)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - _lastSnapshotTime >= _options.SnapshotIntervalOrDefault;
    }

    private void EnqueueTail(Queue<CollabOpBatch> tail, CollabOpBatch batch)
    {
        if (_options.TailOpCount <= 0)
        {
            return;
        }

        if (tail.Count >= _options.TailOpCount)
        {
            tail.Dequeue();
        }

        tail.Enqueue(batch);
    }

    private void RewriteOpLog(Queue<CollabOpBatch> tail)
    {
        if (_options.TailOpCount <= 0 || tail.Count == 0)
        {
            if (File.Exists(_opLogPath))
            {
                File.Delete(_opLogPath);
            }

            return;
        }

        if (File.Exists(_opLogPath))
        {
            File.Delete(_opLogPath);
        }

        using var writer = new CollabOpLogWriter(_opLogPath);
        foreach (var batch in tail)
        {
            writer.Append(batch);
        }
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

    private void WriteSnapshotIndex(long snapshotVersion)
    {
        var opLogLength = 0L;
        if (File.Exists(_opLogPath))
        {
            opLogLength = new FileInfo(_opLogPath).Length;
        }

        var index = new CollabSnapshotIndex(snapshotVersion, opLogLength, DateTimeOffset.UtcNow);
        var payload = _indexSerializer.Serialize(index);
        File.WriteAllBytes(_snapshotIndexPath, payload);
    }

    private FileStream AcquireLock()
    {
        var lockPath = _opLogPath + ".lock";
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}

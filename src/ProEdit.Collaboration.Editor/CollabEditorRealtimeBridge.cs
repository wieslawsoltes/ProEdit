using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ProEdit.Collaboration.Protocol;

namespace ProEdit.Collaboration.Editor;

/// <summary>
/// Bridges realtime collaboration sessions to editor application logic.
/// </summary>
public sealed class CollabEditorRealtimeBridge : IDisposable
{
    private readonly ICollabRealtimeSession _session;
    private readonly EditorCollabApplier _applier;
    private readonly CollabOpHistory _history;
    private readonly SynchronizationContext? _syncContext;
    private readonly Func<Guid, string?>? _authorResolver;
    private readonly Action? _onResyncRequired;
    private readonly ConcurrentQueue<CollabOpBatch> _pendingBatches = new();
    private int _flushScheduled;
    private bool _disposed;

    public CollabEditorRealtimeBridge(
        ICollabRealtimeSession session,
        EditorCollabApplier applier,
        CollabOpHistory history,
        SynchronizationContext? syncContext = null,
        Func<Guid, string?>? authorResolver = null,
        Action? onResyncRequired = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _syncContext = syncContext;
        _authorResolver = authorResolver;
        _onResyncRequired = onResyncRequired;
        _session.OpsReceived += OnOpsReceived;
    }

    private void OnOpsReceived(object? sender, CollabOpsReceivedEventArgs e)
    {
        _pendingBatches.Enqueue(e.Batch);
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 1)
        {
            return;
        }

        if (_syncContext is null)
        {
            _ = Task.Run(FlushPending);
            return;
        }

        _syncContext.Post(_ => FlushPending(), null);
    }

    private void FlushPending()
    {
        try
        {
            if (_disposed)
            {
                DrainQueue();
                return;
            }

            var applyGroups = new List<(IReadOnlyList<ICollabOp> Ops, string? Author)>();
            while (_pendingBatches.TryDequeue(out var batch))
            {
                var result = _history.TransformRemote(batch);
                if (result.RequiresResync)
                {
                    DrainQueue();
                    _onResyncRequired?.Invoke();
                    return;
                }

                if (result.Ops.Count == 0)
                {
                    continue;
                }

                var author = _authorResolver?.Invoke(batch.ActorId);
                applyGroups.Add((result.Ops, author));
            }

            if (applyGroups.Count == 0)
            {
                return;
            }

            _applier.ApplyRemoteOpGroups(applyGroups, refreshLayout: true);
        }
        finally
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
            if (!_pendingBatches.IsEmpty)
            {
                ScheduleFlush();
            }
        }
    }

    private void DrainQueue()
    {
        while (_pendingBatches.TryDequeue(out _))
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.OpsReceived -= OnOpsReceived;
    }
}

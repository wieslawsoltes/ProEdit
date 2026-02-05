using Vibe.Office.Collaboration;
using Vibe.Office.Editing;

namespace Vibe.Office.Collaboration.Editor;

public sealed class CollabUndoRedoService : IUndoRedoService
{
    private readonly ICollabSession _session;
    private readonly ICollabBatchFactory _batchFactory;
    private readonly Guid _actorId;
    private readonly Func<CollabOpBatch, CollabOpHistory.CollabTransformResult>? _transform;
    private readonly Action<IReadOnlyList<ICollabOp>>? _applyLocal;
    private readonly Action<CollabOpBatch>? _onBatchSubmitted;
    private readonly Func<EditorSessionSnapshot, ValueTask>? _applySnapshotAsync;
    private readonly Stack<CollabHistoryEntry> _undo = new();
    private readonly Stack<CollabHistoryEntry> _redo = new();
    private bool _isReplaying;

    public CollabUndoRedoService(
        ICollabSession session,
        ICollabBatchFactory batchFactory,
        Guid actorId,
        Func<CollabOpBatch, CollabOpHistory.CollabTransformResult>? transform = null,
        Action<IReadOnlyList<ICollabOp>>? applyLocal = null,
        Action<CollabOpBatch>? onBatchSubmitted = null,
        Func<EditorSessionSnapshot, ValueTask>? applySnapshotAsync = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _batchFactory = batchFactory ?? throw new ArgumentNullException(nameof(batchFactory));
        _actorId = actorId;
        _transform = transform;
        _applyLocal = applyLocal;
        _onBatchSubmitted = onBatchSubmitted;
        _applySnapshotAsync = applySnapshotAsync;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsReplaying => _isReplaying;

    public void Record(IReadOnlyList<ICollabOp> forwardOps, IReadOnlyList<ICollabOp> inverseOps, long baseVersion)
    {
        if (_isReplaying)
        {
            return;
        }

        _undo.Push(new CollabHistoryEntry(CollabHistoryEntryKind.Ops, forwardOps, inverseOps, baseVersion, default, default));
        _redo.Clear();
    }

    public void RecordSnapshot(EditorSessionSnapshot before, EditorSessionSnapshot after)
    {
        if (_isReplaying)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        _undo.Push(new CollabHistoryEntry(CollabHistoryEntryKind.Snapshot, null, null, 0, before, after));
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public async ValueTask UndoAsync()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var entry = _undo.Pop();
        _isReplaying = true;
        try
        {
            if (entry.Kind == CollabHistoryEntryKind.Snapshot)
            {
                if (_applySnapshotAsync is not null)
                {
                    await _applySnapshotAsync(entry.BeforeSnapshot);
                }

                _redo.Push(entry);
                return;
            }

            if (entry.InverseOps is null || entry.InverseOps.Count == 0)
            {
                return;
            }

            var transformedOps = TransformOps(entry.InverseOps, entry.BaseVersion);
            if (transformedOps is null || transformedOps.Count == 0)
            {
                _undo.Push(entry);
                return;
            }

            var batch = _batchFactory.Create(transformedOps);
            _applyLocal?.Invoke(transformedOps);
            await _session.SubmitLocalAsync(batch);
            _onBatchSubmitted?.Invoke(batch);
            _redo.Push(entry with { BaseVersion = batch.BaseVersion });
        }
        finally
        {
            _isReplaying = false;
        }
    }

    public async ValueTask RedoAsync()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var entry = _redo.Pop();
        _isReplaying = true;
        try
        {
            if (entry.Kind == CollabHistoryEntryKind.Snapshot)
            {
                if (_applySnapshotAsync is not null)
                {
                    await _applySnapshotAsync(entry.AfterSnapshot);
                }

                _undo.Push(entry);
                return;
            }

            if (entry.ForwardOps is null || entry.ForwardOps.Count == 0)
            {
                return;
            }

            var transformedOps = TransformOps(entry.ForwardOps, entry.BaseVersion);
            if (transformedOps is null || transformedOps.Count == 0)
            {
                _redo.Push(entry);
                return;
            }

            var batch = _batchFactory.Create(transformedOps);
            _applyLocal?.Invoke(transformedOps);
            await _session.SubmitLocalAsync(batch);
            _onBatchSubmitted?.Invoke(batch);
            _undo.Push(entry with { BaseVersion = batch.BaseVersion });
        }
        finally
        {
            _isReplaying = false;
        }
    }

    private IReadOnlyList<ICollabOp>? TransformOps(IReadOnlyList<ICollabOp> ops, long baseVersion)
    {
        if (_transform is null)
        {
            return ops;
        }

        var batch = new CollabOpBatch(Guid.NewGuid(), _actorId, baseVersion, 0, 0, DateTimeOffset.UtcNow, ops);
        var result = _transform(batch);
        if (result.RequiresResync)
        {
            return null;
        }

        return result.Ops;
    }

    private enum CollabHistoryEntryKind
    {
        Ops,
        Snapshot
    }

    private readonly record struct CollabHistoryEntry(
        CollabHistoryEntryKind Kind,
        IReadOnlyList<ICollabOp>? ForwardOps,
        IReadOnlyList<ICollabOp>? InverseOps,
        long BaseVersion,
        EditorSessionSnapshot BeforeSnapshot,
        EditorSessionSnapshot AfterSnapshot);
}

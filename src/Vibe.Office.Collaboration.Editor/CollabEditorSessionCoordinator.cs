using System.Collections.Generic;
using System.Threading;
using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Persistence;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Office.Collaboration.Editor;

/// <summary>
/// Coordinates collaboration session state between editor, transport, and op history.
/// </summary>
public sealed class CollabEditorSessionCoordinator : IDisposable
{
    private readonly CollabOpHistory _history;
    private readonly CollabBatchFactory _batchFactory;
    private readonly CollabUndoRedoService _undoRedoService;
    private readonly CollabCommandObserver _commandObserver;
    private readonly CollabEditorIntegration _integration;
    private readonly CollabEditorRealtimeBridge _bridge;
    private readonly CollabSnapshotSerializer _snapshotSerializer = new();
    private readonly CollabDocumentDiff _documentDiff = new();
    private readonly IEditorMutableSession _session;
    private readonly ICollabRealtimeSession _collabSession;
    private readonly SynchronizationContext? _syncContext;
    private readonly Dictionary<Guid, GestureSnapshotState> _gestureSnapshots = new();
    private bool _disposed;

    public CollabEditorSessionCoordinator(
        EditorServices services,
        EditorCommandDispatcher dispatcher,
        IEditorMutableSession session,
        ICollabRealtimeSession collabSession,
        SynchronizationContext? syncContext = null,
        Func<Guid, string?>? authorResolver = null,
        Action? onResyncRequired = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(collabSession);

        _session = session;
        _collabSession = collabSession;
        _syncContext = syncContext;
        _history = new CollabOpHistory();
        _batchFactory = new CollabBatchFactory(collabSession.SenderId, () => _history.Version);
        var applier = new EditorCollabApplier(session);

        _undoRedoService = new CollabUndoRedoService(
            collabSession,
            _batchFactory,
            collabSession.SenderId,
            batch => _history.Transform(batch),
            ops => applier.ApplyRemoteOps(ops, authorResolver?.Invoke(collabSession.SenderId)),
            batch => _history.AppendLocal(batch),
            ApplySnapshotAsync);

        _commandObserver = new CollabCommandObserver(
            session,
            collabSession,
            _undoRedoService,
            _batchFactory,
            batch => _history.AppendLocal(batch),
            snapshotPublisher: ApplySnapshotAsync);

        _integration = CollabEditorIntegration.Attach(
            services,
            dispatcher,
            _commandObserver,
            _undoRedoService,
            applier);

        _bridge = new CollabEditorRealtimeBridge(
            collabSession,
            applier,
            _history,
            syncContext,
            authorResolver,
            onResyncRequired);

        _collabSession.SnapshotReceived += OnSnapshotReceived;
        _integration.GestureRecorder.GestureStarted += OnGestureStarted;
        _integration.GestureRecorder.GestureEnded += OnGestureEnded;
    }

    public CollabOpHistory History => _history;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bridge.Dispose();
        _integration.Dispose();
        _collabSession.SnapshotReceived -= OnSnapshotReceived;
        _integration.GestureRecorder.GestureStarted -= OnGestureStarted;
        _integration.GestureRecorder.GestureEnded -= OnGestureEnded;
    }

    private void OnGestureStarted(object? sender, CollabGestureEventArgs e)
    {
        if (_undoRedoService.IsReplaying)
        {
            return;
        }

        var snapshot = CaptureSnapshot();
        _gestureSnapshots[e.Token.Id] = new GestureSnapshotState(snapshot, _session.DirtyVersion);
    }

    private void OnGestureEnded(object? sender, CollabGestureEventArgs e)
    {
        if (!_gestureSnapshots.TryGetValue(e.Token.Id, out var state))
        {
            return;
        }

        _gestureSnapshots.Remove(e.Token.Id);
        if (_session.DirtyVersion == state.DirtyVersion)
        {
            return;
        }

        var after = CaptureSnapshot();
        if (_documentDiff.TryBuildOps(state.Snapshot.Document, after.Document, out var forwardOps, out var inverseOps)
            && forwardOps.Count > 0
            && inverseOps.Count > 0)
        {
            var batch = _batchFactory.Create(forwardOps);
            _undoRedoService.Record(forwardOps, inverseOps, batch.BaseVersion);
            _history.AppendLocal(batch);
            _ = _collabSession.SubmitLocalAsync(batch);
            return;
        }

        _undoRedoService.RecordSnapshot(state.Snapshot, after);
        _ = ApplySnapshotAsync(after);
    }

    private void OnSnapshotReceived(object? sender, CollabSnapshotReceivedEventArgs e)
    {
        Dispatch(async () =>
        {
            var snapshot = _snapshotSerializer.DeserializeSnapshot(e.Payload.Span);
            DocumentClone.Copy(snapshot.Document, _session.Document);
            _session.RefreshLayout();
            _history.Reset(snapshot.Version);
            _undoRedoService.Clear();
        });
    }

    private async ValueTask ApplySnapshotAsync(EditorSessionSnapshot snapshot)
    {
        DocumentClone.Copy(snapshot.Document, _session.Document);
        _session.RefreshLayout();
        _session.SetSelection(snapshot.Selection);

        var version = _history.Version + 1;
        var collabSnapshot = CollabSnapshot.Create(version, _session.Document);
        var payload = _snapshotSerializer.Serialize(collabSnapshot);
        await _collabSession.SendSnapshotAsync(new SnapshotMessage(collabSnapshot.SnapshotId, collabSnapshot.Version, payload));
        _history.Reset(collabSnapshot.Version);
    }

    private EditorSessionSnapshot CaptureSnapshot()
    {
        var document = DocumentClone.Clone(_session.Document);
        return new EditorSessionSnapshot(document, _session.Selection, _session.Caret);
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

    private readonly record struct GestureSnapshotState(EditorSessionSnapshot Snapshot, long DirtyVersion);
}

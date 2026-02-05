using System.Threading;
using Vibe.Office.Collaboration.Protocol;

namespace Vibe.Office.Collaboration.Editor;

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
        Dispatch(() =>
        {
            var result = _history.TransformRemote(e.Batch);
            if (result.RequiresResync)
            {
                _onResyncRequired?.Invoke();
                return;
            }

            if (result.Ops.Count == 0)
            {
                return;
            }

            var author = _authorResolver?.Invoke(e.Batch.ActorId);
            _applier.ApplyRemoteOps(result.Ops, author);
        });
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

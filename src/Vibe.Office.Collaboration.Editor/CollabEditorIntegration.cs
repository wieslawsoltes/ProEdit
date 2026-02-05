using Vibe.Office.Collaboration;
using Vibe.Office.Editing;

namespace Vibe.Office.Collaboration.Editor;

public sealed class CollabEditorIntegration
    : IDisposable
{
    public CollabCommandObserver CommandObserver { get; }
    public CollabUndoRedoService UndoRedoService { get; }
    public EditorCollabApplier RemoteApplier { get; }
    public CollabGestureRecorder GestureRecorder { get; }
    private readonly EditorServices _services;
    private readonly EditorCommandDispatcher _dispatcher;
    private readonly IEditorCommandHistory? _previousHistory;
    private readonly IEditorCommandExecutionObserver? _previousObserver;
    private readonly IUndoRedoService? _previousUndoRedo;
    private readonly ICollabGestureRecorder? _previousGestureRecorder;
    private bool _disposed;

    private CollabEditorIntegration(
        EditorServices services,
        EditorCommandDispatcher dispatcher,
        CollabCommandObserver commandObserver,
        CollabUndoRedoService undoRedoService,
        EditorCollabApplier remoteApplier,
        CollabGestureRecorder gestureRecorder,
        IEditorCommandHistory? previousHistory,
        IEditorCommandExecutionObserver? previousObserver,
        IUndoRedoService? previousUndoRedo,
        ICollabGestureRecorder? previousGestureRecorder)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        CommandObserver = commandObserver;
        UndoRedoService = undoRedoService;
        RemoteApplier = remoteApplier;
        GestureRecorder = gestureRecorder;
        _previousHistory = previousHistory;
        _previousObserver = previousObserver;
        _previousUndoRedo = previousUndoRedo;
        _previousGestureRecorder = previousGestureRecorder;
    }

    public static CollabEditorIntegration Attach(
        EditorServices services,
        EditorCommandDispatcher dispatcher,
        IEditorMutableSession session,
        ICollabSession collabSession,
        Guid actorId,
        Func<long>? baseVersionProvider = null,
        Action<int>? onLocalApplied = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(collabSession);

        var batchFactory = new CollabBatchFactory(actorId, baseVersionProvider ?? (() => 0));
        var remoteApplier = new EditorCollabApplier(session);
        var undoRedo = new CollabUndoRedoService(collabSession, batchFactory, actorId, transform: null, applyLocal: ops => remoteApplier.ApplyRemoteOps(ops));
        var observer = new CollabCommandObserver(session, collabSession, undoRedo, batchFactory, onLocalApplied: onLocalApplied);
        var gestureRecorder = new CollabGestureRecorder();

        return Attach(services, dispatcher, observer, undoRedo, remoteApplier, gestureRecorder);
    }

    public static CollabEditorIntegration Attach(
        EditorServices services,
        EditorCommandDispatcher dispatcher,
        CollabCommandObserver commandObserver,
        CollabUndoRedoService undoRedoService,
        EditorCollabApplier remoteApplier,
        CollabGestureRecorder? gestureRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(commandObserver);
        ArgumentNullException.ThrowIfNull(undoRedoService);
        ArgumentNullException.ThrowIfNull(remoteApplier);

        var recorder = gestureRecorder ?? new CollabGestureRecorder();
        var existingObserver = dispatcher.ExecutionObserver;
        dispatcher.ExecutionObserver = existingObserver is null
            ? commandObserver
            : new CompositeCommandExecutionObserver(new[] { existingObserver, commandObserver });

        var previousHistory = dispatcher.History;
        dispatcher.History = null;
        services.TryGet<IUndoRedoService>(out var previousUndoRedo);
        services.TryGet<ICollabGestureRecorder>(out var previousGestureRecorder);
        services.Register<IUndoRedoService>(undoRedoService);
        services.Register<ICollabGestureRecorder>(recorder);

        return new CollabEditorIntegration(
            services,
            dispatcher,
            commandObserver,
            undoRedoService,
            remoteApplier,
            recorder,
            previousHistory,
            existingObserver,
            previousUndoRedo,
            previousGestureRecorder);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispatcher.ExecutionObserver = _previousObserver;
        _dispatcher.History = _previousHistory;

        if (_previousUndoRedo is not null)
        {
            _services.Register(_previousUndoRedo);
        }
        else
        {
            _services.Remove<IUndoRedoService>();
        }

        if (_previousGestureRecorder is not null)
        {
            _services.Register(_previousGestureRecorder);
        }
        else
        {
            _services.Remove<ICollabGestureRecorder>();
        }
    }
}

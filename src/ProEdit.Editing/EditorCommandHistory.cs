using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.Editing;

public sealed class EditorCommandHistory : IEditorCommandHistory, IUndoRedoService
{
    private readonly IEditorMutableSession _session;
    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();
    private bool _isRestoring;

    public EditorCommandHistory(IEditorMutableSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int Version { get; private set; }

    public bool ShouldRecord(IEditorCommand command)
    {
        return !_isRestoring && command is IEditorUndoableCommand undoable && undoable.IsUndoable;
    }

    public void ExecuteWithHistory(IEditorMutableSession session, IEditorCommand command, Action execute)
    {
        if (_isRestoring)
        {
            execute();
            return;
        }

        var before = CaptureSnapshot(session);
        execute();
        var after = CaptureSnapshot(session);
        _undo.Push(new HistoryEntry(before, after));
        _redo.Clear();
        Version++;
    }

    public ValueTask UndoAsync()
    {
        if (_undo.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        var entry = _undo.Pop();
        _isRestoring = true;
        RestoreSnapshot(entry.Before);
        _redo.Push(entry);
        _isRestoring = false;
        Version++;
        return ValueTask.CompletedTask;
    }

    public ValueTask RedoAsync()
    {
        if (_redo.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        var entry = _redo.Pop();
        _isRestoring = true;
        RestoreSnapshot(entry.After);
        _undo.Push(entry);
        _isRestoring = false;
        Version++;
        return ValueTask.CompletedTask;
    }

    public void RecordSnapshot(EditorSessionSnapshot before, EditorSessionSnapshot after)
    {
        if (_isRestoring)
        {
            return;
        }

        _undo.Push(new HistoryEntry(before, after));
        _redo.Clear();
        Version++;
    }

    private EditorSessionSnapshot CaptureSnapshot(IEditorMutableSession session)
    {
        var document = DocumentClone.Clone(session.Document);
        return new EditorSessionSnapshot(document, session.Selection, session.Caret);
    }

    private void RestoreSnapshot(EditorSessionSnapshot snapshot)
    {
        DocumentClone.Copy(snapshot.Document, _session.Document);
        _session.RefreshLayout();
        _session.SetSelection(snapshot.Selection);
    }

    private readonly record struct HistoryEntry(EditorSessionSnapshot Before, EditorSessionSnapshot After);
}

public readonly record struct EditorSessionSnapshot(Document Document, TextRange Selection, TextPosition Caret);

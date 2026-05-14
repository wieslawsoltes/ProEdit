using ProEdit.Editing;

namespace ProEdit.Word.Editor.Editing;

public sealed class EditorUndoRedoServiceAdapter : IUndoRedoService
{
    private readonly Func<bool>? _canUndo;
    private readonly Func<bool>? _canRedo;
    private readonly Func<ValueTask>? _undo;
    private readonly Func<ValueTask>? _redo;

    public EditorUndoRedoServiceAdapter(
        Func<bool>? canUndo = null,
        Func<bool>? canRedo = null,
        Func<ValueTask>? undo = null,
        Func<ValueTask>? redo = null)
    {
        _canUndo = canUndo;
        _canRedo = canRedo;
        _undo = undo;
        _redo = redo;
    }

    public bool CanUndo => _canUndo?.Invoke() ?? false;
    public bool CanRedo => _canRedo?.Invoke() ?? false;

    public ValueTask UndoAsync()
    {
        return _undo is null ? ValueTask.CompletedTask : _undo();
    }

    public ValueTask RedoAsync()
    {
        return _redo is null ? ValueTask.CompletedTask : _redo();
    }
}

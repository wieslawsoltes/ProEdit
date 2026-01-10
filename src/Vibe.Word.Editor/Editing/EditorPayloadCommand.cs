using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

internal sealed class EditorPayloadCommand : IEditorUndoableCommand
{
    private readonly object? _payload;
    private readonly Action<IEditorMutableSession, object?> _execute;
    public bool IsUndoable { get; }

    public EditorPayloadCommand(object? payload, Action<IEditorMutableSession, object?> execute, bool isUndoable)
    {
        _payload = payload;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        IsUndoable = isUndoable;
    }

    public void Execute(IEditorMutableSession session)
    {
        _execute(session, _payload);
    }
}

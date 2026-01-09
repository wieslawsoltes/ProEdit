using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

internal sealed class EditorPayloadCommand : IEditorCommand
{
    private readonly object? _payload;
    private readonly Action<IEditorMutableSession, object?> _execute;

    public EditorPayloadCommand(object? payload, Action<IEditorMutableSession, object?> execute)
    {
        _payload = payload;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public void Execute(IEditorMutableSession session)
    {
        _execute(session, _payload);
    }
}

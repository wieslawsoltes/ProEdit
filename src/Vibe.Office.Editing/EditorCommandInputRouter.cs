namespace Vibe.Office.Editing;

public sealed class EditorCommandInputRouter : IEditorInputRouter
{
    private readonly EditorCommandDispatcher _dispatcher;
    private readonly IEditorMutableSession _session;

    public EditorCommandInputRouter(EditorCommandDispatcher dispatcher, IEditorMutableSession session)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool HandleTextInput(ReadOnlySpan<char> text, EditorModifiers modifiers)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        if (text.Length == 1 && (text[0] == '\r' || text[0] == '\n'))
        {
            _dispatcher.Dispatch(new InsertParagraphBreakCommand(), _session);
            return true;
        }

        _dispatcher.Dispatch(new InsertTextCommand(text.ToString()), _session);
        return true;
    }

    public bool HandleKey(EditorKey key, EditorKeyEventKind kind, EditorModifiers modifiers)
    {
        if (kind != EditorKeyEventKind.Down)
        {
            return false;
        }

        var extend = (modifiers & EditorModifiers.Shift) != 0;
        switch (key)
        {
            case EditorKey.Backspace:
                _dispatcher.Dispatch(new BackspaceCommand(), _session);
                return true;
            case EditorKey.Delete:
                _dispatcher.Dispatch(new DeleteForwardCommand(), _session);
                return true;
            case EditorKey.Enter:
                _dispatcher.Dispatch(new InsertParagraphBreakCommand(), _session);
                return true;
            case EditorKey.Left:
                _dispatcher.Dispatch(new MoveLeftCommand(extend), _session);
                return true;
            case EditorKey.Right:
                _dispatcher.Dispatch(new MoveRightCommand(extend), _session);
                return true;
            case EditorKey.Up:
                _dispatcher.Dispatch(new MoveUpCommand(extend), _session);
                return true;
            case EditorKey.Down:
                _dispatcher.Dispatch(new MoveDownCommand(extend), _session);
                return true;
            default:
                return false;
        }
    }

    public bool HandlePointer(in EditorPointerEvent pointerEvent)
    {
        if (pointerEvent.Kind == EditorPointerKind.Down)
        {
            var extend = (pointerEvent.Modifiers & EditorModifiers.Shift) != 0;
            _dispatcher.Dispatch(new SetCaretFromPointCommand(pointerEvent.X, pointerEvent.Y, extend), _session);
            return true;
        }

        if (pointerEvent.Kind == EditorPointerKind.Move && pointerEvent.Button == EditorPointerButton.Primary)
        {
            _dispatcher.Dispatch(new SetCaretFromPointCommand(pointerEvent.X, pointerEvent.Y, true), _session);
            return true;
        }

        return false;
    }
}

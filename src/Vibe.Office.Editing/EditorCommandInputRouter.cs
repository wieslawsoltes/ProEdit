namespace Vibe.Office.Editing;

public sealed class EditorCommandInputRouter : IEditorInputRouter
{
    private readonly EditorCommandDispatcher _dispatcher;
    private readonly IEditorMutableSession _session;
    private readonly IUndoRedoService? _undoRedo;
    private readonly IClipboardService? _clipboard;
    private readonly ISelectionTextService? _selectionText;

    public EditorCommandInputRouter(
        EditorCommandDispatcher dispatcher,
        IEditorMutableSession session,
        IUndoRedoService? undoRedo = null,
        IClipboardService? clipboard = null,
        ISelectionTextService? selectionText = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _undoRedo = undoRedo;
        _clipboard = clipboard;
        _selectionText = selectionText;
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

        if (TryHandleUndoRedo(key, modifiers))
        {
            return true;
        }

        if (TryHandleClipboard(key, modifiers))
        {
            return true;
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

    private bool TryHandleClipboard(EditorKey key, EditorModifiers modifiers)
    {
        if (_clipboard is null)
        {
            return false;
        }

        var modifier = (modifiers & (EditorModifiers.Control | EditorModifiers.Meta)) != 0;
        if (!modifier || (modifiers & EditorModifiers.Alt) != 0)
        {
            return false;
        }

        switch (key)
        {
            case EditorKey.C:
                return CopySelection();
            case EditorKey.X:
                return CutSelection();
            case EditorKey.V:
                return PasteClipboard();
            default:
                return false;
        }
    }

    private bool CopySelection()
    {
        if (!_clipboard!.CanCopy)
        {
            return true;
        }

        if (_selectionText is null)
        {
            return true;
        }

        if (_selectionText.TryGetSelectionText(out var text))
        {
            if (!string.IsNullOrEmpty(text))
            {
                _clipboard.SetText(text);
            }
        }

        return true;
    }

    private bool CutSelection()
    {
        if (!_clipboard!.CanCut)
        {
            return true;
        }

        if (!CopySelection())
        {
            return true;
        }

        if (!_session.Selection.IsEmpty)
        {
            _dispatcher.Dispatch(new BackspaceCommand(), _session);
        }

        return true;
    }

    private bool PasteClipboard()
    {
        if (!_clipboard!.CanPaste)
        {
            return true;
        }

        if (!_clipboard.TryGetText(out var text))
        {
            return true;
        }

        return PasteText(text.AsSpan());
    }

    private bool PasteText(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return true;
        }

        var lineStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var value = text[i];
            if (value != '\r' && value != '\n')
            {
                continue;
            }

            var segment = text.Slice(lineStart, i - lineStart);
            if (!segment.IsEmpty)
            {
                _dispatcher.Dispatch(new InsertTextCommand(segment.ToString()), _session);
            }

            _dispatcher.Dispatch(new InsertParagraphBreakCommand(), _session);

            if (value == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            lineStart = i + 1;
        }

        if (lineStart <= text.Length - 1)
        {
            var tail = text.Slice(lineStart);
            if (!tail.IsEmpty)
            {
                _dispatcher.Dispatch(new InsertTextCommand(tail.ToString()), _session);
            }
        }

        return true;
    }

    private bool TryHandleUndoRedo(EditorKey key, EditorModifiers modifiers)
    {
        if (_undoRedo is null)
        {
            return false;
        }

        var modifier = (modifiers & (EditorModifiers.Control | EditorModifiers.Meta)) != 0;
        if (!modifier)
        {
            return false;
        }

        if (key == EditorKey.Z)
        {
            if ((modifiers & EditorModifiers.Shift) != 0)
            {
                _undoRedo.RedoAsync().GetAwaiter().GetResult();
            }
            else
            {
                _undoRedo.UndoAsync().GetAwaiter().GetResult();
            }

            return true;
        }

        if (key == EditorKey.Y)
        {
            _undoRedo.RedoAsync().GetAwaiter().GetResult();
            return true;
        }

        return false;
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

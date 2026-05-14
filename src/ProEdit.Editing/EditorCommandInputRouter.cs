using ProEdit.Documents;

namespace ProEdit.Editing;

public sealed class EditorCommandInputRouter : IEditorInputRouter
{
    private readonly EditorCommandDispatcher _dispatcher;
    private readonly IEditorMutableSession _session;
    private readonly IUndoRedoService? _undoRedo;
    private readonly IClipboardService? _clipboard;
    private readonly EditorClipboardController? _clipboardController;
    private readonly IContentControlInteractionService? _contentControls;
    private readonly IAutoCorrectService? _autoCorrect;
    private readonly Func<bool> _acceptsTabProvider;
    private readonly Func<bool> _acceptsReturnProvider;
    private readonly Func<bool> _isReadOnlyProvider;

    public EditorCommandInputRouter(
        EditorCommandDispatcher dispatcher,
        IEditorMutableSession session,
        IUndoRedoService? undoRedo = null,
        IClipboardService? clipboard = null,
        ITableSelectionSnapshotProvider? tableSelectionProvider = null,
        IContentControlInteractionService? contentControls = null,
        IAutoCorrectService? autoCorrect = null,
        Func<bool>? acceptsTabProvider = null,
        Func<bool>? acceptsReturnProvider = null,
        Func<bool>? isReadOnlyProvider = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _undoRedo = undoRedo;
        _clipboard = clipboard;
        _contentControls = contentControls;
        _autoCorrect = autoCorrect;
        _acceptsTabProvider = acceptsTabProvider ?? (() => false);
        _acceptsReturnProvider = acceptsReturnProvider ?? (() => true);
        _isReadOnlyProvider = isReadOnlyProvider ?? (() => false);
        if (clipboard is not null)
        {
            _clipboardController = new EditorClipboardController(session, clipboard, tableSelectionProvider);
        }
    }

    public bool HandleTextInput(ReadOnlySpan<char> text, EditorModifiers modifiers)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        if (_isReadOnlyProvider())
        {
            return false;
        }

        if ((text.Length == 1 && (text[0] == '\r' || text[0] == '\n'))
            || (text.Length == 2 && text[0] == '\r' && text[1] == '\n'))
        {
            if (!_acceptsReturnProvider())
            {
                return false;
            }

            _dispatcher.Dispatch(new InsertParagraphBreakCommand(), _session);
            return true;
        }

        if (text.Length == 1 && text[0] == ' '
            && _session is IContentControlInteractionSession contentSession
            && contentSession.TryGetContentControlAtCaret(out var hit)
            && hit.Properties.DataType != ContentControlDataType.None)
        {
            _dispatcher.Dispatch(
                new ActivateContentControlCommand(hit, ContentControlActivationSource.Keyboard, modifiers, _contentControls),
                _session);
            return true;
        }

        _dispatcher.Dispatch(new InsertTextCommand(text.ToString()), _session);
        TryApplyAutoCorrect(text);
        return true;
    }

    private void TryApplyAutoCorrect(ReadOnlySpan<char> insertedText)
    {
        if (_autoCorrect is null)
        {
            return;
        }

        if (!_autoCorrect.TryGetReplacement(_session, insertedText, out var replacement))
        {
            return;
        }

        var start = new TextPosition(replacement.ParagraphIndex, replacement.StartOffset);
        var end = new TextPosition(replacement.ParagraphIndex, replacement.StartOffset + replacement.Length);
        _session.SetSelection(new TextRange(start, end), SelectionUpdateMode.Replace);
        _dispatcher.Dispatch(new InsertTextCommand(replacement.Replacement), _session);
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
                if (_isReadOnlyProvider())
                {
                    return false;
                }

                _dispatcher.Dispatch(new BackspaceCommand(), _session);
                return true;
            case EditorKey.Delete:
                if (_isReadOnlyProvider())
                {
                    return false;
                }

                _dispatcher.Dispatch(new DeleteForwardCommand(), _session);
                return true;
            case EditorKey.Enter:
                if (_isReadOnlyProvider() || !_acceptsReturnProvider())
                {
                    return false;
                }

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
            case EditorKey.Home:
                if (HasDocumentModifier(modifiers))
                {
                    _dispatcher.Dispatch(new MoveDocumentStartCommand(extend), _session);
                }
                else
                {
                    _dispatcher.Dispatch(new MoveLineStartCommand(extend), _session);
                }

                return true;
            case EditorKey.End:
                if (HasDocumentModifier(modifiers))
                {
                    _dispatcher.Dispatch(new MoveDocumentEndCommand(extend), _session);
                }
                else
                {
                    _dispatcher.Dispatch(new MoveLineEndCommand(extend), _session);
                }

                return true;
            case EditorKey.PageUp:
                _dispatcher.Dispatch(new MovePageUpCommand(extend), _session);
                return true;
            case EditorKey.PageDown:
                _dispatcher.Dispatch(new MovePageDownCommand(extend), _session);
                return true;
            case EditorKey.Tab:
                if (_isReadOnlyProvider() || !ShouldInsertTab(modifiers))
                {
                    return false;
                }

                _dispatcher.Dispatch(new InsertTabCommand(), _session);
                return true;
            case EditorKey.A:
                if (!HasDocumentModifier(modifiers) || (modifiers & EditorModifiers.Alt) != 0)
                {
                    return false;
                }

                _dispatcher.Dispatch(new SelectAllCommand(), _session);
                return true;
            default:
                return false;
        }
    }

    private static bool HasDocumentModifier(EditorModifiers modifiers)
    {
        return (modifiers & (EditorModifiers.Control | EditorModifiers.Meta)) != 0;
    }

    private bool ShouldInsertTab(EditorModifiers modifiers)
    {
        if (!_acceptsTabProvider())
        {
            return false;
        }

        return (modifiers & (EditorModifiers.Control | EditorModifiers.Meta | EditorModifiers.Alt)) == 0;
    }

    private bool TryHandleClipboard(EditorKey key, EditorModifiers modifiers)
    {
        if (_clipboard is null || _clipboardController is null)
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
                if (_isReadOnlyProvider())
                {
                    return false;
                }

                return CutSelection();
            case EditorKey.V:
                if (_isReadOnlyProvider())
                {
                    return false;
                }

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

        _clipboardController?.CopySelection();
        return true;
    }

    private bool CutSelection()
    {
        if (!_clipboard!.CanCut)
        {
            return true;
        }

        DispatchClipboardCommand(ClipboardCommandKind.Cut, ClipboardPasteMode.KeepSource);
        return true;
    }

    private bool PasteClipboard()
    {
        if (!_clipboard!.CanPaste)
        {
            return true;
        }

        DispatchClipboardCommand(ClipboardCommandKind.Paste, ClipboardPasteMode.KeepSource);
        return true;
    }

    private void DispatchClipboardCommand(ClipboardCommandKind kind, ClipboardPasteMode mode)
    {
        if (_clipboardController is null)
        {
            return;
        }

        _dispatcher.Dispatch(new ClipboardCommand(_clipboardController, kind, mode), _session);
    }

    private bool TryHandleUndoRedo(EditorKey key, EditorModifiers modifiers)
    {
        if (_undoRedo is null || _isReadOnlyProvider())
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
                if (!_undoRedo.CanRedo)
                {
                    return false;
                }

                _undoRedo.RedoAsync().GetAwaiter().GetResult();
            }
            else
            {
                if (!_undoRedo.CanUndo)
                {
                    return false;
                }

                _undoRedo.UndoAsync().GetAwaiter().GetResult();
            }

            return true;
        }

        if (key == EditorKey.Y)
        {
            if (!_undoRedo.CanRedo)
            {
                return false;
            }

            _undoRedo.RedoAsync().GetAwaiter().GetResult();
            return true;
        }

        return false;
    }

    private enum ClipboardCommandKind
    {
        Cut,
        Paste
    }

    private sealed class ClipboardCommand : IEditorUndoableCommand
    {
        private readonly EditorClipboardController _controller;
        private readonly ClipboardCommandKind _kind;
        private readonly ClipboardPasteMode _mode;

        public ClipboardCommand(EditorClipboardController controller, ClipboardCommandKind kind, ClipboardPasteMode mode)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _kind = kind;
            _mode = mode;
        }

        public bool IsUndoable => true;

        public void Execute(IEditorMutableSession session)
        {
            switch (_kind)
            {
                case ClipboardCommandKind.Cut:
                    _controller.CutSelection();
                    break;
                case ClipboardCommandKind.Paste:
                    _controller.Paste(_mode);
                    break;
            }
        }
    }

    public bool HandlePointer(in EditorPointerEvent pointerEvent)
    {
        if (pointerEvent.Kind == EditorPointerKind.Down)
        {
            var mode = ResolveSelectionMode(pointerEvent.Modifiers);
            if (pointerEvent.Button == EditorPointerButton.Primary && pointerEvent.ClickCount >= 2)
            {
                if (pointerEvent.ClickCount >= 3)
                {
                    _dispatcher.Dispatch(
                        new SelectParagraphFromPointCommand(pointerEvent.X, pointerEvent.Y, SelectionUpdateMode.Replace),
                        _session);
                }
                else
                {
                    _dispatcher.Dispatch(
                        new SelectWordFromPointCommand(pointerEvent.X, pointerEvent.Y, SelectionUpdateMode.Replace),
                        _session);
                }

                return true;
            }

            if (pointerEvent.Button == EditorPointerButton.Primary
                && mode == SelectionUpdateMode.Replace
                && pointerEvent.ClickCount <= 1
                && !_isReadOnlyProvider()
                && _session is IContentControlInteractionSession contentSession
                && contentSession.TryGetContentControlAtPoint(pointerEvent.X, pointerEvent.Y, out var hit)
                && hit.Properties.DataType != ContentControlDataType.None)
            {
                _dispatcher.Dispatch(
                    new ActivateContentControlCommand(hit, ContentControlActivationSource.Pointer, pointerEvent.Modifiers, _contentControls),
                    _session);
                return true;
            }

            _dispatcher.Dispatch(new SetCaretFromPointCommand(pointerEvent.X, pointerEvent.Y, mode), _session);
            return true;
        }

        if (pointerEvent.Kind == EditorPointerKind.Move && pointerEvent.Button == EditorPointerButton.Primary)
        {
            _dispatcher.Dispatch(new SetCaretFromPointCommand(pointerEvent.X, pointerEvent.Y, SelectionUpdateMode.Extend), _session);
            return true;
        }

        return false;
    }

    private static SelectionUpdateMode ResolveSelectionMode(EditorModifiers modifiers)
    {
        var add = (modifiers & (EditorModifiers.Control | EditorModifiers.Meta)) != 0;
        if (add)
        {
            return SelectionUpdateMode.Add;
        }

        var extend = (modifiers & EditorModifiers.Shift) != 0;
        return extend ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
    }
}

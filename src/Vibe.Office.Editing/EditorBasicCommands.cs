namespace Vibe.Office.Editing;

public sealed class InsertTextCommand : IEditorUndoableCommand
{
    public string Text { get; }
    public bool IsUndoable => true;

    public InsertTextCommand(string text)
    {
        Text = text ?? string.Empty;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.InsertText(Text.AsSpan());
    }
}

public sealed class InsertParagraphBreakCommand : IEditorUndoableCommand
{
    public bool IsUndoable => true;

    public void Execute(IEditorMutableSession session)
    {
        session.InsertParagraphBreak();
    }
}

public sealed class BackspaceCommand : IEditorUndoableCommand
{
    public bool IsUndoable => true;

    public void Execute(IEditorMutableSession session)
    {
        session.Backspace();
    }
}

public sealed class DeleteForwardCommand : IEditorUndoableCommand
{
    public bool IsUndoable => true;

    public void Execute(IEditorMutableSession session)
    {
        session.DeleteForward();
    }
}

public sealed class MoveLeftCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MoveLeftCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MoveLeft(ExtendSelection);
    }
}

public sealed class MoveRightCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MoveRightCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MoveRight(ExtendSelection);
    }
}

public sealed class MoveUpCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MoveUpCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MoveUp(ExtendSelection);
    }
}

public sealed class MoveDownCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MoveDownCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MoveDown(ExtendSelection);
    }
}

public sealed class MoveLineStartCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MoveLineStartCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MoveLineStart(ExtendSelection);
    }
}

public sealed class MoveLineEndCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MoveLineEndCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MoveLineEnd(ExtendSelection);
    }
}

public sealed class MoveDocumentStartCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MoveDocumentStartCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MoveDocumentStart(ExtendSelection);
    }
}

public sealed class MoveDocumentEndCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MoveDocumentEndCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MoveDocumentEnd(ExtendSelection);
    }
}

public sealed class MovePageUpCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MovePageUpCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MovePageUp(ExtendSelection);
    }
}

public sealed class MovePageDownCommand : IEditorCommand
{
    public bool ExtendSelection { get; }

    public MovePageDownCommand(bool extendSelection)
    {
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.MovePageDown(ExtendSelection);
    }
}

public sealed class InsertTabCommand : IEditorUndoableCommand
{
    public bool IsUndoable => true;

    public void Execute(IEditorMutableSession session)
    {
        session.InsertText("\t".AsSpan());
    }
}

public sealed class SelectAllCommand : IEditorCommand
{
    public void Execute(IEditorMutableSession session)
    {
        session.SelectAll();
    }
}

public sealed class SetCaretFromPointCommand : IEditorCommand
{
    public float X { get; }
    public float Y { get; }
    public SelectionUpdateMode Mode { get; }
    public bool ExtendSelection => Mode.HasFlag(SelectionUpdateMode.Extend);

    public SetCaretFromPointCommand(float x, float y, bool extendSelection)
    {
        Mode = extendSelection ? SelectionUpdateMode.Extend : SelectionUpdateMode.Replace;
        X = x;
        Y = y;
    }

    public SetCaretFromPointCommand(float x, float y, SelectionUpdateMode mode)
    {
        Mode = mode;
        X = x;
        Y = y;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.SetCaretFromPoint(X, Y, Mode);
    }
}

public sealed class SelectWordFromPointCommand : IEditorCommand
{
    public float X { get; }
    public float Y { get; }
    public SelectionUpdateMode Mode { get; }

    public SelectWordFromPointCommand(float x, float y, SelectionUpdateMode mode)
    {
        X = x;
        Y = y;
        Mode = mode;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.TrySelectWordFromPoint(X, Y, Mode);
    }
}

public sealed class SelectParagraphFromPointCommand : IEditorCommand
{
    public float X { get; }
    public float Y { get; }
    public SelectionUpdateMode Mode { get; }

    public SelectParagraphFromPointCommand(float x, float y, SelectionUpdateMode mode)
    {
        X = x;
        Y = y;
        Mode = mode;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.TrySelectParagraphFromPoint(X, Y, Mode);
    }
}

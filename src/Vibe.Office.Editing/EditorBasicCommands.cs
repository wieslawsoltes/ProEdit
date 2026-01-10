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

public sealed class SetCaretFromPointCommand : IEditorCommand
{
    public float X { get; }
    public float Y { get; }
    public bool ExtendSelection { get; }

    public SetCaretFromPointCommand(float x, float y, bool extendSelection)
    {
        X = x;
        Y = y;
        ExtendSelection = extendSelection;
    }

    public void Execute(IEditorMutableSession session)
    {
        session.SetCaretFromPoint(X, Y, ExtendSelection);
    }
}

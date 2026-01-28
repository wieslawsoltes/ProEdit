namespace Vibe.Office.Editing;

public sealed class BasicEditingModule : IEditorModule
{
    public void Register(EditorModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Commands.Register(new InsertTextCommandHandler());
        context.Commands.Register(new InsertParagraphBreakCommandHandler());
        context.Commands.Register(new BackspaceCommandHandler());
        context.Commands.Register(new DeleteForwardCommandHandler());
        context.Commands.Register(new MoveLeftCommandHandler());
        context.Commands.Register(new MoveRightCommandHandler());
        context.Commands.Register(new MoveUpCommandHandler());
        context.Commands.Register(new MoveDownCommandHandler());
        context.Commands.Register(new SetCaretFromPointCommandHandler());
    }
}

public sealed class InsertTextCommandHandler : EditorCommandHandler<InsertTextCommand>
{
    public override void Handle(IEditorMutableSession session, InsertTextCommand command)
    {
        if (string.IsNullOrEmpty(command.Text))
        {
            return;
        }

        session.InsertText(command.Text.AsSpan());
    }
}

public sealed class InsertParagraphBreakCommandHandler : EditorCommandHandler<InsertParagraphBreakCommand>
{
    public override void Handle(IEditorMutableSession session, InsertParagraphBreakCommand command)
    {
        session.InsertParagraphBreak();
    }
}

public sealed class BackspaceCommandHandler : EditorCommandHandler<BackspaceCommand>
{
    public override void Handle(IEditorMutableSession session, BackspaceCommand command)
    {
        session.Backspace();
    }
}

public sealed class DeleteForwardCommandHandler : EditorCommandHandler<DeleteForwardCommand>
{
    public override void Handle(IEditorMutableSession session, DeleteForwardCommand command)
    {
        session.DeleteForward();
    }
}

public sealed class MoveLeftCommandHandler : EditorCommandHandler<MoveLeftCommand>
{
    public override void Handle(IEditorMutableSession session, MoveLeftCommand command)
    {
        session.MoveLeft(command.ExtendSelection);
    }
}

public sealed class MoveRightCommandHandler : EditorCommandHandler<MoveRightCommand>
{
    public override void Handle(IEditorMutableSession session, MoveRightCommand command)
    {
        session.MoveRight(command.ExtendSelection);
    }
}

public sealed class MoveUpCommandHandler : EditorCommandHandler<MoveUpCommand>
{
    public override void Handle(IEditorMutableSession session, MoveUpCommand command)
    {
        session.MoveUp(command.ExtendSelection);
    }
}

public sealed class MoveDownCommandHandler : EditorCommandHandler<MoveDownCommand>
{
    public override void Handle(IEditorMutableSession session, MoveDownCommand command)
    {
        session.MoveDown(command.ExtendSelection);
    }
}

public sealed class SetCaretFromPointCommandHandler : EditorCommandHandler<SetCaretFromPointCommand>
{
    public override void Handle(IEditorMutableSession session, SetCaretFromPointCommand command)
    {
        session.SetCaretFromPoint(command.X, command.Y, command.Mode);
    }
}

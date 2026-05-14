namespace ProEdit.Editing;

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
        context.Commands.Register(new MoveLineStartCommandHandler());
        context.Commands.Register(new MoveLineEndCommandHandler());
        context.Commands.Register(new MoveDocumentStartCommandHandler());
        context.Commands.Register(new MoveDocumentEndCommandHandler());
        context.Commands.Register(new MovePageUpCommandHandler());
        context.Commands.Register(new MovePageDownCommandHandler());
        context.Commands.Register(new InsertTabCommandHandler());
        context.Commands.Register(new SelectAllCommandHandler());
        context.Commands.Register(new SetCaretFromPointCommandHandler());
        context.Commands.Register(new SelectWordFromPointCommandHandler());
        context.Commands.Register(new SelectParagraphFromPointCommandHandler());
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

public sealed class MoveLineStartCommandHandler : EditorCommandHandler<MoveLineStartCommand>
{
    public override void Handle(IEditorMutableSession session, MoveLineStartCommand command)
    {
        session.MoveLineStart(command.ExtendSelection);
    }
}

public sealed class MoveLineEndCommandHandler : EditorCommandHandler<MoveLineEndCommand>
{
    public override void Handle(IEditorMutableSession session, MoveLineEndCommand command)
    {
        session.MoveLineEnd(command.ExtendSelection);
    }
}

public sealed class MoveDocumentStartCommandHandler : EditorCommandHandler<MoveDocumentStartCommand>
{
    public override void Handle(IEditorMutableSession session, MoveDocumentStartCommand command)
    {
        session.MoveDocumentStart(command.ExtendSelection);
    }
}

public sealed class MoveDocumentEndCommandHandler : EditorCommandHandler<MoveDocumentEndCommand>
{
    public override void Handle(IEditorMutableSession session, MoveDocumentEndCommand command)
    {
        session.MoveDocumentEnd(command.ExtendSelection);
    }
}

public sealed class MovePageUpCommandHandler : EditorCommandHandler<MovePageUpCommand>
{
    public override void Handle(IEditorMutableSession session, MovePageUpCommand command)
    {
        session.MovePageUp(command.ExtendSelection);
    }
}

public sealed class MovePageDownCommandHandler : EditorCommandHandler<MovePageDownCommand>
{
    public override void Handle(IEditorMutableSession session, MovePageDownCommand command)
    {
        session.MovePageDown(command.ExtendSelection);
    }
}

public sealed class InsertTabCommandHandler : EditorCommandHandler<InsertTabCommand>
{
    public override void Handle(IEditorMutableSession session, InsertTabCommand command)
    {
        session.InsertText("\t".AsSpan());
    }
}

public sealed class SelectAllCommandHandler : EditorCommandHandler<SelectAllCommand>
{
    public override void Handle(IEditorMutableSession session, SelectAllCommand command)
    {
        session.SelectAll();
    }
}

public sealed class SetCaretFromPointCommandHandler : EditorCommandHandler<SetCaretFromPointCommand>
{
    public override void Handle(IEditorMutableSession session, SetCaretFromPointCommand command)
    {
        session.SetCaretFromPoint(command.X, command.Y, command.Mode);
    }
}

public sealed class SelectWordFromPointCommandHandler : EditorCommandHandler<SelectWordFromPointCommand>
{
    public override void Handle(IEditorMutableSession session, SelectWordFromPointCommand command)
    {
        session.TrySelectWordFromPoint(command.X, command.Y, command.Mode);
    }
}

public sealed class SelectParagraphFromPointCommandHandler : EditorCommandHandler<SelectParagraphFromPointCommand>
{
    public override void Handle(IEditorMutableSession session, SelectParagraphFromPointCommand command)
    {
        session.TrySelectParagraphFromPoint(command.X, command.Y, command.Mode);
    }
}

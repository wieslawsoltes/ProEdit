namespace Vibe.Office.Editing;

public interface IEditorCommand
{
    void Execute(IEditorMutableSession session);
}

public interface IEditorCommandHandler
{
    void Handle(IEditorMutableSession session, IEditorCommand command);
}

public interface IEditorCommandHandler<in TCommand> : IEditorCommandHandler where TCommand : IEditorCommand
{
    void Handle(IEditorMutableSession session, TCommand command);
}

public abstract class EditorCommandHandler<TCommand> : IEditorCommandHandler<TCommand> where TCommand : IEditorCommand
{
    public abstract void Handle(IEditorMutableSession session, TCommand command);

    void IEditorCommandHandler.Handle(IEditorMutableSession session, IEditorCommand command)
    {
        Handle(session, (TCommand)command);
    }
}

public sealed class EditorCommandDispatcher
{
    private readonly Dictionary<Type, object> _handlers = new();

    public void Register<TCommand>(IEditorCommandHandler<TCommand> handler) where TCommand : IEditorCommand
    {
        _handlers[typeof(TCommand)] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Dispatch(IEditorCommand command, IEditorMutableSession session)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(session);

        if (_handlers.TryGetValue(command.GetType(), out var handler))
        {
            ((IEditorCommandHandler)handler).Handle(session, command);
            return;
        }

        command.Execute(session);
    }
}

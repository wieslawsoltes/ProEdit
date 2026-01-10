using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorCommandRouterAdapter : IEditorCommandRouter
{
    private readonly Dictionary<string, CommandBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly EditorCommandDispatcher _dispatcher;
    private readonly IEditorMutableSession _session;

    public EditorCommandRouterAdapter(EditorCommandDispatcher dispatcher, IEditorMutableSession session)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public void Register<TCommand>(
        string commandId,
        Func<object?, TCommand?> commandFactory,
        Func<object?, bool>? canExecute = null) where TCommand : IEditorCommand
    {
        if (canExecute is null)
        {
            Register(commandId, commandFactory, (Func<RibbonContextSnapshot?, object?, bool>?)null);
            return;
        }

        Register(commandId, commandFactory, (_, payload) => canExecute(payload));
    }

    public void Register<TCommand>(
        string commandId,
        Func<object?, TCommand?> commandFactory,
        Func<RibbonContextSnapshot?, object?, bool>? canExecute) where TCommand : IEditorCommand
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            throw new ArgumentException("Command id is required.", nameof(commandId));
        }

        ArgumentNullException.ThrowIfNull(commandFactory);
        _bindings[commandId] = new CommandBinding(payload => commandFactory(payload), canExecute);
    }

    public void RegisterAction(
        string commandId,
        Action<IEditorMutableSession, object?> execute,
        Func<object?, bool>? canExecute = null,
        bool isUndoable = true)
    {
        if (canExecute is null)
        {
            RegisterAction(commandId, execute, (Func<RibbonContextSnapshot?, object?, bool>?)null, isUndoable);
            return;
        }

        RegisterAction(commandId, execute, (_, payload) => canExecute(payload), isUndoable);
    }

    public void RegisterAction(
        string commandId,
        Action<IEditorMutableSession, object?> execute,
        Func<RibbonContextSnapshot?, object?, bool>? canExecute,
        bool isUndoable = true)
    {
        ArgumentNullException.ThrowIfNull(execute);
        Register(commandId, payload => new EditorPayloadCommand(payload, execute, isUndoable), canExecute);
    }

    public bool CanExecute(string commandId, object? payload = null, RibbonContextSnapshot? context = null)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            return false;
        }

        if (!_bindings.TryGetValue(commandId, out var binding))
        {
            return false;
        }

        return binding.CanExecute?.Invoke(context, payload) ?? true;
    }

    public ValueTask<bool> ExecuteAsync(string commandId, object? payload = null, RibbonContextSnapshot? context = null)
    {
        if (!CanExecute(commandId, payload, context))
        {
            return ValueTask.FromResult(false);
        }

        var binding = _bindings[commandId];
        var command = binding.Factory(payload);
        if (command is null)
        {
            return ValueTask.FromResult(false);
        }

        _dispatcher.Dispatch(command, _session);
        return ValueTask.FromResult(true);
    }

    private readonly record struct CommandBinding(
        Func<object?, IEditorCommand?> Factory,
        Func<RibbonContextSnapshot?, object?, bool>? CanExecute);
}

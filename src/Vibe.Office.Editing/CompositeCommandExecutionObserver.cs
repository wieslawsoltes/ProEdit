namespace Vibe.Office.Editing;

public sealed class CompositeCommandExecutionObserver : IEditorCommandExecutionObserver
{
    private readonly IReadOnlyList<IEditorCommandExecutionObserver> _observers;

    public CompositeCommandExecutionObserver(IEnumerable<IEditorCommandExecutionObserver> observers)
    {
        ArgumentNullException.ThrowIfNull(observers);
        _observers = observers.ToArray();
    }

    public void OnCommandExecuting(IEditorCommand command, IEditorMutableSession session)
    {
        foreach (var observer in _observers)
        {
            observer.OnCommandExecuting(command, session);
        }
    }

    public void OnCommandExecuted(IEditorCommand command, IEditorMutableSession session, bool recordHistory)
    {
        foreach (var observer in _observers)
        {
            observer.OnCommandExecuted(command, session, recordHistory);
        }
    }
}

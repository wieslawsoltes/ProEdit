namespace Vibe.Office.Ribbon;

public sealed class RibbonCommand : IRibbonCommand
{
    private readonly Func<bool>? _canExecute;
    private readonly Func<ValueTask> _execute;

    public RibbonCommand(Func<ValueTask> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RibbonCommand(Action execute, Func<bool>? canExecute = null)
        : this(() =>
        {
            execute();
            return ValueTask.CompletedTask;
        }, canExecute)
    {
    }

    public bool CanExecute() => _canExecute?.Invoke() ?? true;

    public ValueTask ExecuteAsync()
    {
        if (!CanExecute())
        {
            return ValueTask.CompletedTask;
        }

        return _execute();
    }
}

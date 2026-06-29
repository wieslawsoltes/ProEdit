using System.Reactive;
using System.Threading;
using ReactiveUI;

namespace ProEdit.Reporting.Avalonia.Designer;

internal static class DesignerCommandFactory
{
    public static ReactiveCommand<Unit, Unit> Create(Action execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return ReactiveCommand.Create(execute, outputScheduler: RxSchedulers.MainThreadScheduler);
    }

    public static ReactiveCommand<Unit, Unit> CreateFromTask(Func<Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return ReactiveCommand.CreateFromTask(execute, outputScheduler: RxSchedulers.MainThreadScheduler);
    }

    public static ReactiveCommand<Unit, Unit> CreateFromTask(Func<CancellationToken, Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return ReactiveCommand.CreateFromTask(execute, outputScheduler: RxSchedulers.MainThreadScheduler);
    }
}

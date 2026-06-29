using System.Reactive;
using ReactiveUI;
using ProEdit.Reporting.Avalonia;

namespace ProEdit.Reporting.App.ViewModels;

internal sealed partial class ReportingStudioViewModel
{
    private ReportingStudioMode _currentMode;

    public ReportingStudioMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _currentMode, value);
            if (_selectedWorkspaceTabIndex != (value == ReportingStudioMode.Run ? 1 : 0))
            {
                _selectedWorkspaceTabIndex = value == ReportingStudioMode.Run ? 1 : 0;
                this.RaisePropertyChanged(nameof(SelectedWorkspaceTabIndex));
            }

            this.RaisePropertyChanged(nameof(IsDesignMode));
            this.RaisePropertyChanged(nameof(IsRunMode));
        }
    }

    public bool IsDesignMode => CurrentMode == ReportingStudioMode.Design;

    public bool IsRunMode => CurrentMode == ReportingStudioMode.Run;

    public ReactiveCommand<Unit, Unit> ResetActiveLayoutCommand { get; private set; } = null!;

    private void InitializeLayoutShell()
    {
        _currentMode = _selectedWorkspaceTabIndex == 1 ? ReportingStudioMode.Run : ReportingStudioMode.Design;
        ResetActiveLayoutCommand = ReactiveCommand.Create(ResetActiveLayout, outputScheduler: RxSchedulers.MainThreadScheduler);
    }

    private void SynchronizeModeFromWorkspaceIndex()
    {
        var targetMode = _selectedWorkspaceTabIndex == 1
            ? ReportingStudioMode.Run
            : ReportingStudioMode.Design;
        if (_currentMode == targetMode)
        {
            return;
        }

        _currentMode = targetMode;
        this.RaisePropertyChanged(nameof(CurrentMode));
        this.RaisePropertyChanged(nameof(IsDesignMode));
        this.RaisePropertyChanged(nameof(IsRunMode));
    }

    private void ResetActiveLayout()
    {
        if (IsDesignMode)
        {
            DesignerViewModel.ResetLayoutCommand.Execute().Subscribe();
            StatusMessage = "Reset the design workbench layout.";
            return;
        }

        ViewerViewModel.ResetLayoutCommand.Execute().Subscribe();
        StatusMessage = "Reset the run-preview layout.";
    }
}

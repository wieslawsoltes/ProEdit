using System.Reactive;
using ReactiveUI;

namespace ProEdit.Collaboration.UI.ViewModels;

/// <summary>
/// Root view model for collaboration UI surfaces.
/// </summary>
public sealed class CollabShellViewModel : ReactiveObject
{
    private bool _isPaneVisible;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollabShellViewModel"/> class.
    /// </summary>
    public CollabShellViewModel(ICollabUiService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Session = new CollabSessionViewModel(service);
        Participants = new CollabParticipantsViewModel(service);
        Status = new CollabStatusViewModel(service);
        Settings = new CollabSettingsViewModel(service);
        Conflict = new CollabConflictViewModel(service);
        TogglePaneCommand = ReactiveCommand.Create(TogglePane);
        _isPaneVisible = false;
    }

    /// <summary>
    /// Gets the session status view model.
    /// </summary>
    public CollabSessionViewModel Session { get; }

    /// <summary>
    /// Gets the participants view model.
    /// </summary>
    public CollabParticipantsViewModel Participants { get; }

    /// <summary>
    /// Gets the diagnostics view model.
    /// </summary>
    public CollabStatusViewModel Status { get; }

    /// <summary>
    /// Gets the settings view model.
    /// </summary>
    public CollabSettingsViewModel Settings { get; }

    /// <summary>
    /// Gets the conflict view model.
    /// </summary>
    public CollabConflictViewModel Conflict { get; }

    /// <summary>
    /// Gets or sets whether the collaboration pane is visible.
    /// </summary>
    public bool IsPaneVisible
    {
        get => _isPaneVisible;
        set => this.RaiseAndSetIfChanged(ref _isPaneVisible, value);
    }

    /// <summary>
    /// Command to toggle the collaboration pane.
    /// </summary>
    public ReactiveCommand<Unit, Unit> TogglePaneCommand { get; }

    private void TogglePane()
    {
        IsPaneVisible = !IsPaneVisible;
    }
}

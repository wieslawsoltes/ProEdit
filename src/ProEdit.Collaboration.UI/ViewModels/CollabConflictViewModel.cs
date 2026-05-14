using System.Reactive;
using ReactiveUI;

namespace ProEdit.Collaboration.UI.ViewModels;

/// <summary>
/// View model exposing collaboration error state and recovery actions.
/// </summary>
public sealed class CollabConflictViewModel : ReactiveObject
{
    private readonly ICollabUiService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollabConflictViewModel"/> class.
    /// </summary>
    public CollabConflictViewModel(ICollabUiService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        ReconnectCommand = ReactiveCommand.CreateFromTask(() => _service.ReconnectAsync().AsTask());
        DismissCommand = ReactiveCommand.Create(_service.ClearError);
        _service.StateChanged += OnServiceStateChanged;
    }

    /// <summary>
    /// Gets whether an error banner should be shown.
    /// </summary>
    public bool HasError => _service.ConnectionState == CollabConnectionState.Error;

    /// <summary>
    /// Gets the latest error message.
    /// </summary>
    public string? ErrorMessage => _service.ConnectionMessage;

    /// <summary>
    /// Command to attempt reconnection.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ReconnectCommand { get; }

    /// <summary>
    /// Command to dismiss the error banner.
    /// </summary>
    public ReactiveCommand<Unit, Unit> DismissCommand { get; }

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        this.RaisePropertyChanged(nameof(HasError));
        this.RaisePropertyChanged(nameof(ErrorMessage));
    }
}

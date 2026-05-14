using System.Reactive;
using ReactiveUI;

namespace ProEdit.Collaboration.UI.ViewModels;

/// <summary>
/// View model exposing collaboration session status and commands.
/// </summary>
public sealed class CollabSessionViewModel : ReactiveObject
{
    private readonly ICollabUiService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollabSessionViewModel"/> class.
    /// </summary>
    public CollabSessionViewModel(ICollabUiService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        JoinCommand = ReactiveCommand.CreateFromTask(() => _service.JoinAsync().AsTask());
        LeaveCommand = ReactiveCommand.CreateFromTask(() => _service.LeaveAsync().AsTask());
        ShareCommand = ReactiveCommand.CreateFromTask(() => _service.ShareAsync().AsTask());
        ReconnectCommand = ReactiveCommand.CreateFromTask(() => _service.ReconnectAsync().AsTask());
        _service.StateChanged += OnServiceStateChanged;
    }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public CollabConnectionState ConnectionState => _service.ConnectionState;

    /// <summary>
    /// Gets the current document identifier.
    /// </summary>
    public Guid DocumentId => _service.DocumentId;

    /// <summary>
    /// Gets the current session identifier.
    /// </summary>
    public Guid SessionId => _service.SessionId;

    /// <summary>
    /// Gets a user-facing status string.
    /// </summary>
    public string StatusText => ResolveStatusText();

    /// <summary>
    /// Gets the status color as a hex string.
    /// </summary>
    public string StatusColor => ResolveStatusColor();

    /// <summary>
    /// Gets the latest connection message.
    /// </summary>
    public string? ConnectionMessage => _service.ConnectionMessage;

    /// <summary>
    /// Gets whether joining is available.
    /// </summary>
    public bool CanJoin => ConnectionState is CollabConnectionState.Disconnected or CollabConnectionState.Error or CollabConnectionState.Offline;

    /// <summary>
    /// Gets whether leaving is available.
    /// </summary>
    public bool CanLeave => ConnectionState is CollabConnectionState.Connected or CollabConnectionState.Connecting or CollabConnectionState.Reconnecting;

    /// <summary>
    /// Command to join a collaboration session.
    /// </summary>
    public ReactiveCommand<Unit, Unit> JoinCommand { get; }

    /// <summary>
    /// Command to leave a collaboration session.
    /// </summary>
    public ReactiveCommand<Unit, Unit> LeaveCommand { get; }

    /// <summary>
    /// Command to share the collaboration session.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ShareCommand { get; }

    /// <summary>
    /// Command to reconnect to the collaboration session.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ReconnectCommand { get; }

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        this.RaisePropertyChanged(nameof(ConnectionState));
        this.RaisePropertyChanged(nameof(DocumentId));
        this.RaisePropertyChanged(nameof(SessionId));
        this.RaisePropertyChanged(nameof(StatusText));
        this.RaisePropertyChanged(nameof(StatusColor));
        this.RaisePropertyChanged(nameof(ConnectionMessage));
        this.RaisePropertyChanged(nameof(CanJoin));
        this.RaisePropertyChanged(nameof(CanLeave));
    }

    private string ResolveStatusText()
    {
        return ConnectionState switch
        {
            CollabConnectionState.Connected => "Connected",
            CollabConnectionState.Connecting => "Connecting",
            CollabConnectionState.Reconnecting => "Reconnecting",
            CollabConnectionState.Error => "Connection Error",
            CollabConnectionState.Offline => "Offline",
            _ => "Not Connected"
        };
    }

    private string ResolveStatusColor()
    {
        return ConnectionState switch
        {
            CollabConnectionState.Connected => "#2D7DF0",
            CollabConnectionState.Connecting => "#F1C40F",
            CollabConnectionState.Reconnecting => "#F39C12",
            CollabConnectionState.Error => "#E74C3C",
            CollabConnectionState.Offline => "#7F8C8D",
            _ => "#9AA4B2"
        };
    }
}

using ReactiveUI;

namespace Vibe.Office.Collaboration.UI.ViewModels;

/// <summary>
/// View model exposing collaboration transport settings.
/// </summary>
public sealed class CollabSettingsViewModel : ReactiveObject
{
    private readonly ICollabUiService _service;
    private static readonly CollabTransportMode[] SupportedModes =
    {
        CollabTransportMode.LocalBroker,
        CollabTransportMode.SharedFile,
        CollabTransportMode.SharedFileAuto,
        CollabTransportMode.Server
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="CollabSettingsViewModel"/> class.
    /// </summary>
    public CollabSettingsViewModel(ICollabUiService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _service.StateChanged += OnServiceStateChanged;
    }

    /// <summary>
    /// Gets the supported transport modes.
    /// </summary>
    public IReadOnlyList<CollabTransportMode> TransportModes => SupportedModes;

    /// <summary>
    /// Gets or sets the transport mode.
    /// </summary>
    public CollabTransportMode TransportMode
    {
        get => _service.TransportMode;
        set
        {
            _service.TransportMode = value;
            this.RaisePropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the server URL.
    /// </summary>
    public string? ServerUrl
    {
        get => _service.ServerUrl;
        set
        {
            _service.ServerUrl = value;
            this.RaisePropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the shared path.
    /// </summary>
    public string? SharedPath
    {
        get => _service.SharedPath;
        set
        {
            _service.SharedPath = value;
            this.RaisePropertyChanged();
        }
    }

    /// <summary>
    /// Gets the resolved shared path used for the current mode.
    /// </summary>
    public string? ResolvedSharedPath => _service.ResolvedSharedPath;

    /// <summary>
    /// Gets whether the shared path editor should be visible.
    /// </summary>
    public bool ShowSharedPathEditor => TransportMode is CollabTransportMode.SharedFile or CollabTransportMode.SharedFileAuto;

    /// <summary>
    /// Gets the label text for the shared path field.
    /// </summary>
    public string SharedPathLabel => TransportMode == CollabTransportMode.SharedFileAuto
        ? "Join Path (optional)"
        : "Shared Path";

    /// <summary>
    /// Gets whether the resolved shared path should be visible.
    /// </summary>
    public bool ShowResolvedSharedPath => TransportMode == CollabTransportMode.SharedFileAuto;

    /// <summary>
    /// Gets or sets the local broker port.
    /// </summary>
    public int? LocalBrokerPort
    {
        get => _service.LocalBrokerPort;
        set
        {
            _service.LocalBrokerPort = value;
            this.RaisePropertyChanged();
        }
    }

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        this.RaisePropertyChanged(nameof(TransportModes));
        this.RaisePropertyChanged(nameof(TransportMode));
        this.RaisePropertyChanged(nameof(ServerUrl));
        this.RaisePropertyChanged(nameof(SharedPath));
        this.RaisePropertyChanged(nameof(ResolvedSharedPath));
        this.RaisePropertyChanged(nameof(ShowSharedPathEditor));
        this.RaisePropertyChanged(nameof(SharedPathLabel));
        this.RaisePropertyChanged(nameof(ShowResolvedSharedPath));
        this.RaisePropertyChanged(nameof(LocalBrokerPort));
    }
}

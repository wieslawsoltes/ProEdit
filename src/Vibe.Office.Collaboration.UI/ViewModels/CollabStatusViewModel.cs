using ReactiveUI;

namespace Vibe.Office.Collaboration.UI.ViewModels;

/// <summary>
/// View model exposing collaboration diagnostics.
/// </summary>
public sealed class CollabStatusViewModel : ReactiveObject
{
    private readonly ICollabUiService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollabStatusViewModel"/> class.
    /// </summary>
    public CollabStatusViewModel(ICollabUiService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _service.StateChanged += OnServiceStateChanged;
    }

    /// <summary>
    /// Gets the estimated sync lag.
    /// </summary>
    public TimeSpan SyncLag => _service.SyncLag;

    /// <summary>
    /// Gets the outbound op queue depth.
    /// </summary>
    public int OpQueueDepth => _service.OpQueueDepth;

    /// <summary>
    /// Gets the age of the latest snapshot.
    /// </summary>
    public TimeSpan SnapshotAge => _service.SnapshotAge;

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        this.RaisePropertyChanged(nameof(SyncLag));
        this.RaisePropertyChanged(nameof(OpQueueDepth));
        this.RaisePropertyChanged(nameof(SnapshotAge));
    }
}

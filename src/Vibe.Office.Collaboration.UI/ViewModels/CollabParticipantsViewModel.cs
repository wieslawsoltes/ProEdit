using System.Collections.ObjectModel;
using ReactiveUI;

namespace Vibe.Office.Collaboration.UI.ViewModels;

/// <summary>
/// View model exposing collaboration participant information.
/// </summary>
public sealed class CollabParticipantsViewModel : ReactiveObject
{
    private readonly ICollabUiService _service;
    private readonly ObservableCollection<CollabParticipantViewModel> _participants = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CollabParticipantsViewModel"/> class.
    /// </summary>
    public CollabParticipantsViewModel(ICollabUiService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Participants = new ReadOnlyObservableCollection<CollabParticipantViewModel>(_participants);
        _service.StateChanged += OnServiceStateChanged;
        Refresh();
    }

    /// <summary>
    /// Gets the active participants.
    /// </summary>
    public ReadOnlyObservableCollection<CollabParticipantViewModel> Participants { get; }

    /// <summary>
    /// Gets the number of active participants.
    /// </summary>
    public int Count => _participants.Count;

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        _participants.Clear();
        foreach (var participant in _service.Participants)
        {
            _participants.Add(new CollabParticipantViewModel(participant));
        }

        this.RaisePropertyChanged(nameof(Count));
    }
}

/// <summary>
/// View model representing an individual participant.
/// </summary>
public sealed class CollabParticipantViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollabParticipantViewModel"/> class.
    /// </summary>
    public CollabParticipantViewModel(CollabParticipant participant)
    {
        UserId = participant.UserId;
        DisplayName = participant.DisplayName;
        Color = participant.Color;
        LastActiveUtc = participant.LastActiveUtc;
        IsLocal = participant.IsLocal;
    }

    /// <summary>
    /// Gets the user identifier.
    /// </summary>
    public Guid UserId { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the participant color.
    /// </summary>
    public string Color { get; }

    /// <summary>
    /// Gets the last active timestamp (UTC).
    /// </summary>
    public DateTimeOffset LastActiveUtc { get; }

    /// <summary>
    /// Gets whether this participant is the local user.
    /// </summary>
    public bool IsLocal { get; }
}

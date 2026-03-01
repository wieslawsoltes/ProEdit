using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.UI;
using Vibe.Office.WinUICompat.Collaboration;
using Vibe.Office.WinUICompat.Controls;
using Xunit;

namespace Vibe.Office.WinUICompat.Uno.Tests;

public sealed class RichEditBoxCollaborationBehaviorTests
{
    [Fact]
    public void Defaults_AreDisconnected()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();

        Assert.Equal(CollabConnectionState.Disconnected, RichEditBoxCollaborationBehavior.GetConnectionState(box));
        Assert.Null(RichEditBoxCollaborationBehavior.GetConnectionMessage(box));
        Assert.Equal(0, RichEditBoxCollaborationBehavior.GetParticipantCount(box));
        Assert.Equal(0, RichEditBoxCollaborationBehavior.GetPresenceCount(box));
    }

    [Fact]
    public void UiService_UpdatesDiagnostics()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();
        var ui = new FakeCollabUiService();
        ui.SetConnectionState(CollabConnectionState.Connected, "Connected");
        ui.UpdateParticipants(
            new[]
            {
                new CollabParticipant(Guid.NewGuid(), "A", "#111111", DateTimeOffset.UtcNow, false),
                new CollabParticipant(Guid.NewGuid(), "B", "#222222", DateTimeOffset.UtcNow, false)
            });
        ui.UpdatePresence(
            new PresenceState(Guid.NewGuid(), "A", null, null, DateTimeOffset.UtcNow),
            TimeSpan.FromSeconds(5));
        ui.UpdateDiagnostics(TimeSpan.FromSeconds(2), 4, TimeSpan.FromSeconds(10));

        RichEditBoxCollaborationBehavior.SetCollabUiService(box, ui);
        RichEditBoxCollaborationBehavior.SetIsEnabled(box, true);

        Assert.Equal(CollabConnectionState.Connected, RichEditBoxCollaborationBehavior.GetConnectionState(box));
        Assert.Equal("Connected", RichEditBoxCollaborationBehavior.GetConnectionMessage(box));
        Assert.Equal(2, RichEditBoxCollaborationBehavior.GetParticipantCount(box));
        Assert.Equal(1, RichEditBoxCollaborationBehavior.GetPresenceCount(box));
        Assert.Equal(4, RichEditBoxCollaborationBehavior.GetOpQueueDepth(box));
    }

    private sealed class FakeCollabUiService : ICollabUiService
    {
        private readonly List<CollabParticipant> _participants = new();
        private readonly List<PresenceState> _presence = new();

        public CollabConnectionState ConnectionState { get; private set; }

        public string? ConnectionMessage { get; private set; }

        public Guid DocumentId { get; } = Guid.NewGuid();

        public Guid SessionId { get; } = Guid.NewGuid();

        public CollabTransportMode TransportMode { get; set; } = CollabTransportMode.LocalBroker;

        public string? ServerUrl { get; set; }

        public string? SharedPath { get; set; }

        public string? ResolvedSharedPath { get; private set; }

        public int? LocalBrokerPort { get; set; }

        public IReadOnlyList<CollabParticipant> Participants => _participants;

        public IReadOnlyList<PresenceState> Presence => _presence;

        public TimeSpan SyncLag { get; private set; }

        public int OpQueueDepth { get; private set; }

        public TimeSpan SnapshotAge { get; private set; }

        public event EventHandler? StateChanged;

        public ValueTask JoinAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ShareAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public void UpdatePresence(PresenceState presence, TimeSpan? timeToLive = null)
        {
            _presence.Clear();
            _presence.Add(presence);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateParticipants(IReadOnlyList<CollabParticipant> participants)
        {
            _participants.Clear();
            _participants.AddRange(participants);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateDiagnostics(TimeSpan syncLag, int opQueueDepth, TimeSpan snapshotAge)
        {
            SyncLag = syncLag;
            OpQueueDepth = opQueueDepth;
            SnapshotAge = snapshotAge;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetConnectionState(CollabConnectionState state, string? message = null)
        {
            ConnectionState = state;
            ConnectionMessage = message;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearError()
        {
            if (ConnectionState == CollabConnectionState.Error)
            {
                ConnectionState = CollabConnectionState.Connected;
            }

            ConnectionMessage = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

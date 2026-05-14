using ProEdit.Collaboration;
using ProEdit.Collaboration.Persistence;
using ProEdit.Collaboration.UI;
using ProEdit.Documents;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class CollabUiStateTests
{
    [Fact]
    public async Task JoinSetsSessionAndConnectionState()
    {
        var identity = new DefaultCollabIdentityService();
        var state = new CollabUiState(identity)
        {
            TransportMode = CollabTransportMode.SharedFile,
            SharedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };
        await state.JoinAsync();

        Assert.Equal(CollabConnectionState.Connected, state.ConnectionState);
        Assert.NotEqual(Guid.Empty, state.SessionId);
        Assert.NotEqual(Guid.Empty, state.DocumentId);

        await state.LeaveAsync();
    }

    [Fact]
    public async Task JoinSeedsSharedFileSnapshotWhenEmpty()
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "doc");
        var snapshotPath = basePath + CollabPersistedFormat.SnapshotExtension;
        var snapshotIndexPath = basePath + CollabPersistedFormat.SnapshotIndexExtension;

        var state = new CollabUiState(new DefaultCollabIdentityService())
        {
            TransportMode = CollabTransportMode.SharedFile,
            SharedPath = basePath
        };

        var serializer = new CollabSnapshotSerializer();
        state.ConfigureSnapshotSeedProvider(() =>
        {
            var snapshot = CollabSnapshot.Create(0, new Document());
            return serializer.Serialize(snapshot);
        });

        await state.JoinAsync();

        Assert.True(File.Exists(snapshotPath));
        Assert.True(new FileInfo(snapshotPath).Length > 0);
        Assert.True(File.Exists(snapshotIndexPath));
        Assert.True(new FileInfo(snapshotIndexPath).Length > 0);

        await state.LeaveAsync();
    }

    [Fact]
    public void UpdateParticipantsAssignsFallbackColor()
    {
        var state = new CollabUiState();
        var participant = new CollabParticipant(Guid.NewGuid(), "User", string.Empty, DateTimeOffset.UtcNow, false);

        state.UpdateParticipants(new[] { participant });

        Assert.Single(state.Participants);
        Assert.False(string.IsNullOrWhiteSpace(state.Participants[0].Color));
    }

    [Fact]
    public void UpdatePresenceAddsEntry()
    {
        var state = new CollabUiState();
        var userId = Guid.NewGuid();
        var presence = new PresenceState(userId, "User", null, null, DateTimeOffset.UtcNow, "#FF0000");

        state.UpdatePresence(presence, TimeSpan.FromSeconds(5));

        Assert.Contains(state.Presence, p => p.UserId == userId);
    }
}

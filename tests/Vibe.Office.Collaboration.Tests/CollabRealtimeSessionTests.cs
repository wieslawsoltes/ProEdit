using System.Threading.Tasks;
using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.Collaboration.Transports;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class CollabRealtimeSessionTests
{
    [Fact]
    public async Task PresenceFromRemoteUserIsRaised()
    {
        var transport = new InMemoryLoopbackTransport();
        var options = new CollabRealtimeSessionOptions
        {
            DocumentId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            SenderId = Guid.NewGuid()
        };

        var session = new CollabRealtimeSession(options, transport);
        var tcs = new TaskCompletionSource<CollabPresenceEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.PresenceReceived += (_, args) => tcs.TrySetResult(args);

        await session.ConnectAsync();

        var remoteUserId = Guid.NewGuid();
        var presence = new PresenceState(remoteUserId, "Remote", null, null, DateTimeOffset.UtcNow, "#FF00FF");
        var envelope = new CollabEnvelope<PresenceMessage>(
            CollabProtocolVersion.V1,
            options.DocumentId,
            Guid.NewGuid(),
            remoteUserId,
            1,
            1,
            DateTimeOffset.UtcNow,
            CollabMessageType.Presence,
            new PresenceMessage(presence, TimeSpan.FromSeconds(5)));

        await transport.SendAsync(CollabProtocolJsonCodec.Serialize(envelope));

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(remoteUserId, result.Presence.UserId);
        Assert.Equal(TimeSpan.FromSeconds(5), result.TimeToLive);
    }
}

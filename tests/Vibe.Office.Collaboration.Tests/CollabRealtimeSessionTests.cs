using System.Threading;
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

    [Fact]
    public async Task PresencePayloadIsCompressedWhenEnabled()
    {
        var transport = new CaptureTransport();
        var options = new CollabRealtimeSessionOptions
        {
            DocumentId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            Compression = CollabMessageCompression.BrotliCompressionId,
            CompressionThresholdBytes = 0
        };

        var session = new CollabRealtimeSession(options, transport);
        await session.ConnectAsync();
        transport.LastPayload = null;

        var presence = new PresenceState(options.SenderId, "Local", null, null, DateTimeOffset.UtcNow, "#00FF00");
        await session.SendPresenceAsync(presence, TimeSpan.FromSeconds(5));

        Assert.NotNull(transport.LastPayload);
        Assert.True(CollabMessageCompression.TryReadHeader(transport.LastPayload.Value.Span, out _));
    }

    [Fact]
    public async Task CompressedPresenceFromRemoteUserIsRaised()
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
        var presence = new PresenceState(remoteUserId, "Remote", null, null, DateTimeOffset.UtcNow, "#00FF00");
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

        var payload = CollabProtocolJsonCodec.Serialize(envelope);
        var compressed = CollabMessageCompression.MaybeCompress(payload, CollabMessageCompression.BrotliCompressionId, thresholdBytes: 0);
        await transport.SendAsync(compressed);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(remoteUserId, result.Presence.UserId);
        Assert.Equal(TimeSpan.FromSeconds(5), result.TimeToLive);
    }

    private sealed class CaptureTransport : ICollabTransportConnection
    {
        public ReadOnlyMemory<byte>? LastPayload { get; set; }

        public event EventHandler<CollabTransportMessageEventArgs>? MessageReceived;
        public event EventHandler<CollabTransportStateChangedEventArgs>? StateChanged;

        public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            LastPayload = payload;
            MessageReceived?.Invoke(this, new CollabTransportMessageEventArgs(payload));
            return ValueTask.CompletedTask;
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Connected));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Disconnected));
            return ValueTask.CompletedTask;
        }
    }
}

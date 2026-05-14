using System.Text.Json;
using ProEdit.Collaboration;
using ProEdit.Collaboration.Protocol;
using ProEdit.Collaboration.Server;
using ProEdit.Collaboration.Transports.WebSocket;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class WebSocketRelayTests
{
    private static readonly TimeSpan RelayReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RelayReceiveTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RelaysOpsBetweenDesktopAndBrowserClients()
    {
        var serverOptions = new CollabWebSocketServerOptions
        {
            Urls = new[] { "http://127.0.0.1:0" }
        };

        await using var server = new CollabWebSocketServer(serverOptions);
        await server.StartAsync();
        var endpoint = server.WebSocketEndpoint ?? throw new InvalidOperationException("WebSocket endpoint not available.");

        await using var clientA = new WebSocketClientTransport(endpoint);
        await using var clientB = new WebSocketClientTransport(endpoint);

        var docId = Guid.NewGuid();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var senderA = Guid.NewGuid();
        var senderB = Guid.NewGuid();

        var received = new TaskCompletionSource<CollabEnvelope<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clientB.MessageReceived += (_, args) =>
        {
            var envelope = CollabProtocolJsonCodec.DeserializeEnvelope(args.Payload.Span);
            if (envelope.MessageType == CollabMessageType.Ops)
            {
                received.TrySetResult(envelope);
            }
        };
        clientA.MessageReceived += (_, args) =>
        {
            var envelope = CollabProtocolJsonCodec.DeserializeEnvelope(args.Payload.Span);
            if (envelope.MessageType == CollabMessageType.Presence && envelope.SenderId == senderB)
            {
                ready.TrySetResult();
            }
        };

        await clientA.ConnectAsync();
        await clientB.ConnectAsync();

        await SendHelloJoinAsync(clientA, docId, sessionA, senderA);
        await SendHelloJoinAsync(clientB, docId, sessionB, senderB);

        await WaitForRelayReadyAsync(clientB, docId, sessionB, senderB, ready);

        var anchor = TextAnchor.Before(Guid.NewGuid(), 0);
        var batch = CollabOpBatch.Create(senderA, 0, 1, 1, new ICollabOp[] { new InsertTextOp(anchor, "hi", senderA) });
        var opsEnvelope = new CollabEnvelope<OpsMessage>(
            CollabProtocolVersion.V1,
            docId,
            sessionA,
            senderA,
            2,
            2,
            DateTimeOffset.UtcNow,
            CollabMessageType.Ops,
            new OpsMessage(batch));

        await clientA.SendAsync(CollabProtocolJsonCodec.Serialize(opsEnvelope));

        var receivedEnvelope = await received.Task.WaitAsync(RelayReceiveTimeout);
        Assert.Equal(CollabMessageType.Ops, receivedEnvelope.MessageType);

        var ops = CollabProtocolJsonCodec.DeserializePayload<OpsMessage>(receivedEnvelope.Payload);
        Assert.Equal(batch.BatchId, ops.Batch.BatchId);
    }

    [Fact]
    public async Task ThrottlesPresenceUpdates()
    {
        var serverOptions = new CollabWebSocketServerOptions
        {
            Urls = new[] { "http://127.0.0.1:0" },
            PresenceThrottleInterval = TimeSpan.FromMilliseconds(250)
        };

        await using var server = new CollabWebSocketServer(serverOptions);
        await server.StartAsync();
        var endpoint = server.WebSocketEndpoint ?? throw new InvalidOperationException("WebSocket endpoint not available.");

        await using var clientA = new WebSocketClientTransport(endpoint);
        await using var clientB = new WebSocketClientTransport(endpoint);

        var docId = Guid.NewGuid();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var senderA = Guid.NewGuid();
        var senderB = Guid.NewGuid();

        var presenceCount = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clientA.MessageReceived += (_, args) =>
        {
            var envelope = CollabProtocolJsonCodec.DeserializeEnvelope(args.Payload.Span);
            if (envelope.MessageType == CollabMessageType.Presence && envelope.SenderId == senderB)
            {
                ready.TrySetResult();
            }
        };
        clientB.MessageReceived += (_, args) =>
        {
            var envelope = CollabProtocolJsonCodec.DeserializeEnvelope(args.Payload.Span);
            if (envelope.MessageType == CollabMessageType.Presence && envelope.SenderId == senderA && envelope.Sequence is 3 or 4)
            {
                Interlocked.Increment(ref presenceCount);
            }
        };

        await clientA.ConnectAsync();
        await clientB.ConnectAsync();

        await SendHelloJoinAsync(clientA, docId, sessionA, senderA);
        await SendHelloJoinAsync(clientB, docId, sessionB, senderB);
        await WaitForRelayReadyAsync(clientB, docId, sessionB, senderB, ready);

        var presence = new PresenceState(senderA, "User A", null, null, DateTimeOffset.UtcNow, "#FF0000");
        var message = new PresenceMessage(presence, TimeSpan.FromSeconds(5));

        var envelope1 = new CollabEnvelope<PresenceMessage>(
            CollabProtocolVersion.V1,
            docId,
            sessionA,
            senderA,
            3,
            3,
            DateTimeOffset.UtcNow,
            CollabMessageType.Presence,
            message);

        var envelope2 = envelope1 with { Sequence = 4, Lamport = 4, TimestampUtc = DateTimeOffset.UtcNow.AddMilliseconds(10) };

        await clientA.SendAsync(CollabProtocolJsonCodec.Serialize(envelope1));
        await clientA.SendAsync(CollabProtocolJsonCodec.Serialize(envelope2));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (Volatile.Read(ref presenceCount) < 1 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        await Task.Delay(serverOptions.PresenceThrottleInterval + TimeSpan.FromMilliseconds(150));
        Assert.Equal(1, Volatile.Read(ref presenceCount));
    }

    private static async Task SendHelloJoinAsync(WebSocketClientTransport client, Guid documentId, Guid sessionId, Guid senderId)
    {
        var hello = new CollabEnvelope<HelloMessage>(
            CollabProtocolVersion.V1,
            documentId,
            sessionId,
            senderId,
            1,
            1,
            DateTimeOffset.UtcNow,
            CollabMessageType.Hello,
            new HelloMessage("Test", Array.Empty<string>(), null));

        var join = new CollabEnvelope<JoinMessage>(
            CollabProtocolVersion.V1,
            documentId,
            sessionId,
            senderId,
            2,
            2,
            DateTimeOffset.UtcNow,
            CollabMessageType.Join,
            new JoinMessage(documentId, 0, null));

        await client.SendAsync(CollabProtocolJsonCodec.Serialize(hello));
        await client.SendAsync(CollabProtocolJsonCodec.Serialize(join));
    }

    private static async Task WaitForRelayReadyAsync(
        WebSocketClientTransport sender,
        Guid documentId,
        Guid sessionId,
        Guid senderId,
        TaskCompletionSource ready)
    {
        var attempt = 0;
        while (!ready.Task.IsCompleted && attempt < 20)
        {
            attempt++;
            var sequence = (long)(100 + attempt);
            var presenceEnvelope = new CollabEnvelope<PresenceMessage>(
                CollabProtocolVersion.V1,
                documentId,
                sessionId,
                senderId,
                sequence,
                sequence,
                DateTimeOffset.UtcNow,
                CollabMessageType.Presence,
                new PresenceMessage(
                    new PresenceState(senderId, "Relay Ready", null, null, DateTimeOffset.UtcNow, "#00FF00"),
                    TimeSpan.FromSeconds(5)));

            await sender.SendAsync(CollabProtocolJsonCodec.Serialize(presenceEnvelope));
            if (ready.Task.IsCompleted)
            {
                break;
            }

            try
            {
                await ready.Task.WaitAsync(TimeSpan.FromMilliseconds(120));
            }
            catch (TimeoutException)
            {
                // Retry until both clients are observed in the relay group.
            }
        }

        await ready.Task.WaitAsync(RelayReadyTimeout);
    }
}

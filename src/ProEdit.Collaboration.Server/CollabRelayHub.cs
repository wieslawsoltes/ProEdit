using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ProEdit.Collaboration.Protocol;

namespace ProEdit.Collaboration.Server;

internal sealed class CollabRelayHub : IAsyncDisposable
{
    private readonly CollabWebSocketServerOptions _options;
    private readonly ICollabServerAuthenticator _authenticator;
    private readonly Dictionary<Guid, CollabDocumentGroup> _documents = new();
    private readonly object _sync = new();

    public CollabRelayHub(CollabWebSocketServerOptions options)
    {
        _options = options;
        _authenticator = options.Authenticator ?? new AllowAllAuthenticator();
    }

    public async Task HandleConnectionAsync(HttpContext context, WebSocket socket, CancellationToken cancellationToken)
    {
        var connection = new CollabServerConnection(context, socket, _options, _authenticator, this);
        await connection.RunAsync(cancellationToken);
    }

    internal void Join(Guid documentId, CollabServerConnection connection)
    {
        lock (_sync)
        {
            if (!_documents.TryGetValue(documentId, out var group))
            {
                group = new CollabDocumentGroup();
                _documents[documentId] = group;
            }

            group.Add(connection);
        }
    }

    internal void Leave(Guid documentId, CollabServerConnection connection)
    {
        lock (_sync)
        {
            if (!_documents.TryGetValue(documentId, out var group))
            {
                return;
            }

            group.Remove(connection);
            if (group.Count == 0)
            {
                _documents.Remove(documentId);
            }
        }
    }

    internal async Task BroadcastAsync(Guid documentId, CollabServerConnection sender, ReadOnlyMemory<byte> payload)
    {
        List<CollabServerConnection> recipients;
        lock (_sync)
        {
            if (!_documents.TryGetValue(documentId, out var group))
            {
                return;
            }

            recipients = group.Snapshot();
        }

        foreach (var connection in recipients)
        {
            if (ReferenceEquals(connection, sender))
            {
                continue;
            }

            await connection.SendAsync(payload);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _documents.Clear();
        }

        return ValueTask.CompletedTask;
    }

    internal sealed class CollabServerConnection
    {
        private readonly HttpContext _context;
        private readonly WebSocket _socket;
        private readonly CollabWebSocketServerOptions _options;
        private readonly ICollabServerAuthenticator _authenticator;
        private readonly CollabRelayHub _hub;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly byte[] _buffer;
        private bool _helloReceived;
        private bool _joined;
        private Guid _documentId;
        private DateTimeOffset _lastPresenceForwarded;

        public CollabServerConnection(
            HttpContext context,
            WebSocket socket,
            CollabWebSocketServerOptions options,
            ICollabServerAuthenticator authenticator,
            CollabRelayHub hub)
        {
            _context = context;
            _socket = socket;
            _options = options;
            _authenticator = authenticator;
            _hub = hub;
            _buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    var payload = await ReadMessageAsync(cancellationToken);
                    if (payload is null)
                    {
                        break;
                    }

                    await HandleMessageAsync(payload.Value, cancellationToken);
                }
            }
            finally
            {
                if (_joined)
                {
                    _hub.Leave(_documentId, this);
                }

                ArrayPool<byte>.Shared.Return(_buffer);
                _sendLock.Dispose();
                await CloseAsync();
            }
        }

        public async Task SendAsync(ReadOnlyMemory<byte> payload)
        {
            if (_socket.State != WebSocketState.Open)
            {
                return;
            }

            await _sendLock.WaitAsync();
            try
            {
                await _socket.SendAsync(payload, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task HandleMessageAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            CollabEnvelope<JsonElement> envelope;
            ReadOnlyMemory<byte> parsedPayload = payload;
            CollabCompressionHeader? compressionHeader = null;
            try
            {
                if (CollabMessageCompression.TryDecompress(payload.Span, _options.MaxDecompressedBytes, out var decompressed, out var header))
                {
                    parsedPayload = decompressed;
                    compressionHeader = header;
                }

                envelope = CollabProtocolJsonCodec.DeserializeEnvelope(parsedPayload.Span);
            }
            catch (Exception ex)
            {
                await SendErrorAsync(Guid.Empty, Guid.Empty, Guid.Empty, "invalid_payload", ex.Message);
                return;
            }

            if (envelope.ProtocolVersion != CollabProtocolVersion.V1)
            {
                await SendErrorAsync(envelope.DocumentId, envelope.SessionId, envelope.SenderId, "unsupported_version",
                    $"Protocol version {envelope.ProtocolVersion} is not supported.");
                return;
            }

            switch (envelope.MessageType)
            {
                case CollabMessageType.Hello:
                    await HandleHelloAsync(envelope);
                    break;
                case CollabMessageType.Join:
                    await HandleJoinAsync(envelope);
                    break;
                case CollabMessageType.Presence:
                    await HandlePresenceAsync(envelope, payload, compressionHeader);
                    break;
                case CollabMessageType.Ops:
                case CollabMessageType.Snapshot:
                case CollabMessageType.Ack:
                case CollabMessageType.Leave:
                    await ForwardAsync(envelope, payload);
                    break;
                case CollabMessageType.Error:
                    break;
                default:
                    await SendErrorAsync(envelope.DocumentId, envelope.SessionId, envelope.SenderId, "unsupported_message",
                        $"Message type {envelope.MessageType} is not supported.");
                    break;
            }
        }

        private async Task HandleHelloAsync(CollabEnvelope<JsonElement> envelope)
        {
            if (_helloReceived)
            {
                return;
            }

            var hello = CollabProtocolJsonCodec.DeserializePayload<HelloMessage>(envelope.Payload);
            var result = await _authenticator.AuthorizeHelloAsync(new CollabServerHelloContext(_context,
                new CollabEnvelope<HelloMessage>(
                    envelope.ProtocolVersion,
                    envelope.DocumentId,
                    envelope.SessionId,
                    envelope.SenderId,
                    envelope.Sequence,
                    envelope.Lamport,
                    envelope.TimestampUtc,
                    envelope.MessageType,
                    hello)));

            if (!result.IsAllowed)
            {
                await SendErrorAsync(envelope.DocumentId, envelope.SessionId, envelope.SenderId,
                    result.ErrorCode ?? "unauthorized",
                    result.ErrorMessage ?? "Hello rejected.");
                await CloseAsync();
                return;
            }

            _helloReceived = true;
        }

        private async Task HandleJoinAsync(CollabEnvelope<JsonElement> envelope)
        {
            if (!_helloReceived)
            {
                await SendErrorAsync(envelope.DocumentId, envelope.SessionId, envelope.SenderId, "missing_hello",
                    "Hello message required before join.");
                return;
            }

            var join = CollabProtocolJsonCodec.DeserializePayload<JoinMessage>(envelope.Payload);
            if (envelope.DocumentId != Guid.Empty && envelope.DocumentId != join.DocumentId)
            {
                await SendErrorAsync(envelope.DocumentId, envelope.SessionId, envelope.SenderId, "document_mismatch",
                    "Envelope documentId does not match join payload.");
                return;
            }

            if (join.DocumentId == Guid.Empty)
            {
                await SendErrorAsync(envelope.DocumentId, envelope.SessionId, envelope.SenderId, "invalid_document",
                    "DocumentId must be provided.");
                return;
            }

            if (envelope.SessionId == Guid.Empty || envelope.SenderId == Guid.Empty)
            {
                await SendErrorAsync(join.DocumentId, envelope.SessionId, envelope.SenderId, "invalid_identity",
                    "SessionId and SenderId must be provided.");
                return;
            }

            var result = await _authenticator.AuthorizeJoinAsync(new CollabServerJoinContext(_context,
                new CollabEnvelope<JoinMessage>(
                    envelope.ProtocolVersion,
                    join.DocumentId,
                    envelope.SessionId,
                    envelope.SenderId,
                    envelope.Sequence,
                    envelope.Lamport,
                    envelope.TimestampUtc,
                    envelope.MessageType,
                    join)));

            if (!result.IsAllowed)
            {
                await SendErrorAsync(join.DocumentId, envelope.SessionId, envelope.SenderId,
                    result.ErrorCode ?? "unauthorized",
                    result.ErrorMessage ?? "Join rejected.");
                await CloseAsync();
                return;
            }

            _documentId = join.DocumentId;
            if (!_joined)
            {
                _hub.Join(_documentId, this);
            }

            _joined = true;
        }

        private async Task HandlePresenceAsync(
            CollabEnvelope<JsonElement> envelope,
            ReadOnlyMemory<byte> payload,
            CollabCompressionHeader? compressionHeader)
        {
            if (!_joined)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _lastPresenceForwarded < _options.PresenceThrottleInterval)
            {
                return;
            }

            _lastPresenceForwarded = now;

            var presence = CollabProtocolJsonCodec.DeserializePayload<PresenceMessage>(envelope.Payload);
            if (presence.TimeToLive <= TimeSpan.Zero)
            {
                return;
            }

            var clampedTtl = ClampPresenceTtl(presence.TimeToLive);
            if (clampedTtl == presence.TimeToLive)
            {
                await ForwardAsync(envelope, payload);
                return;
            }

            var updatedEnvelope = new CollabEnvelope<PresenceMessage>(
                envelope.ProtocolVersion,
                envelope.DocumentId,
                envelope.SessionId,
                envelope.SenderId,
                envelope.Sequence,
                envelope.Lamport,
                envelope.TimestampUtc,
                envelope.MessageType,
                presence with { TimeToLive = clampedTtl });

            var adjustedPayload = CollabProtocolJsonCodec.Serialize(updatedEnvelope);
            var outgoing = compressionHeader.HasValue
                ? CollabMessageCompression.MaybeCompress(adjustedPayload, compressionHeader.Value.Algorithm, thresholdBytes: 0)
                : adjustedPayload;
            await _hub.BroadcastAsync(_documentId, this, outgoing);
        }

        private async Task ForwardAsync(CollabEnvelope<JsonElement> envelope, ReadOnlyMemory<byte> payload)
        {
            if (!_joined)
            {
                return;
            }

            if (envelope.DocumentId != Guid.Empty && envelope.DocumentId != _documentId)
            {
                return;
            }

            await _hub.BroadcastAsync(_documentId, this, payload);
        }

        private TimeSpan ClampPresenceTtl(TimeSpan ttl)
        {
            if (ttl < _options.MinimumPresenceTimeToLive)
            {
                return _options.MinimumPresenceTimeToLive;
            }

            if (ttl > _options.MaximumPresenceTimeToLive)
            {
                return _options.MaximumPresenceTimeToLive;
            }

            return ttl;
        }

        private async Task<ReadOnlyMemory<byte>?> ReadMessageAsync(CancellationToken cancellationToken)
        {
            var writer = new ArrayBufferWriter<byte>();
            while (true)
            {
                var result = await _socket.ReceiveAsync(_buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                writer.Write(_buffer.AsSpan(0, result.Count));
                if (writer.WrittenCount > _options.MaxMessageBytes)
                {
                    await SendErrorAsync(_documentId, Guid.Empty, Guid.Empty, "message_too_large",
                        $"Message exceeded {_options.MaxMessageBytes} bytes.");
                    return null;
                }

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return writer.WrittenMemory.ToArray();
        }

        private async Task SendErrorAsync(Guid documentId, Guid sessionId, Guid senderId, string code, string message)
        {
            var envelope = new CollabEnvelope<ErrorMessage>(
                CollabProtocolVersion.V1,
                documentId,
                sessionId,
                senderId,
                0,
                0,
                DateTimeOffset.UtcNow,
                CollabMessageType.Error,
                new ErrorMessage(code, message));

            var payload = CollabProtocolJsonCodec.Serialize(envelope);
            await SendAsync(payload);
        }

        private Task CloseAsync()
        {
            if (_socket.State == WebSocketState.Open)
            {
                return _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CollabDocumentGroup
    {
        private readonly List<CollabServerConnection> _connections = new();
        private readonly object _sync = new();

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _connections.Count;
                }
            }
        }

        public void Add(CollabServerConnection connection)
        {
            lock (_sync)
            {
                if (!_connections.Contains(connection))
                {
                    _connections.Add(connection);
                }
            }
        }

        public void Remove(CollabServerConnection connection)
        {
            lock (_sync)
            {
                _connections.Remove(connection);
            }
        }

        public List<CollabServerConnection> Snapshot()
        {
            lock (_sync)
            {
                return _connections.ToList();
            }
        }
    }
}

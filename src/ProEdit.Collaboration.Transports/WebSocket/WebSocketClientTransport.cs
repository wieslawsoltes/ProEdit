using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using ProEdit.Collaboration;

namespace ProEdit.Collaboration.Transports.WebSocket;

public sealed class WebSocketClientTransport : ICollabTransportConnection, IAsyncDisposable
{
    private readonly Uri _uri;
    private readonly int _maxMessageBytes;
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _readLoop;

    public WebSocketClientTransport(Uri uri, int maxMessageBytes = 4 * 1024 * 1024)
    {
        _uri = uri ?? throw new ArgumentNullException(nameof(uri));
        _maxMessageBytes = maxMessageBytes;
    }

    public event EventHandler<CollabTransportMessageEventArgs>? MessageReceived;
    public event EventHandler<CollabTransportStateChangedEventArgs>? StateChanged;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_socket.State == WebSocketState.Open)
        {
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Connecting));
        await _socket.ConnectAsync(_uri, cancellationToken);
        StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Connected));
        _readLoop = ReadLoopAsync(_cts.Token);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
        {
            _cts?.Cancel();
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Ignore shutdown races where the socket is already aborted.
            }
        }

        StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Disconnected));
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (_socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Transport is not connected.");
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var payload = await ReadMessageAsync(buffer, cancellationToken);
                if (payload is null)
                {
                    break;
                }

                MessageReceived?.Invoke(this, new CollabTransportMessageEventArgs(payload.Value));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Error, ex.Message));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await DisconnectAsync();
        }
    }

    private async Task<ReadOnlyMemory<byte>?> ReadMessageAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            writer.Write(buffer.AsSpan(0, result.Count));
            if (writer.WrittenCount > _maxMessageBytes)
            {
                throw new InvalidDataException($"WebSocket message exceeded max size {_maxMessageBytes} bytes.");
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return writer.WrittenMemory.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
        _sendLock.Dispose();
        _socket.Dispose();
    }
}

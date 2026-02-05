using System.Net.Sockets;
using Vibe.Office.Collaboration;

namespace Vibe.Office.Collaboration.Transports.Local;

public sealed class LocalBrokerClientTransport : ICollabTransportConnection, IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    public LocalBrokerClientTransport(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public event EventHandler<CollabTransportMessageEventArgs>? MessageReceived;
    public event EventHandler<CollabTransportStateChangedEventArgs>? StateChanged;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _client = new TcpClient();
        StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Connecting));
        await _client.ConnectAsync(_host, _port, cancellationToken);
        _stream = _client.GetStream();
        StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Connected));
        _ = ReadLoopAsync(_cts.Token);
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Disconnected));
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Transport is not connected.");
        }

        var lengthBytes = BitConverter.GetBytes(payload.Length);
        _stream.Write(lengthBytes, 0, lengthBytes.Length);
        _stream.Write(payload.Span);
        _stream.Flush();
        return ValueTask.CompletedTask;
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var lengthBuffer = await ReadExactAsync(_stream, sizeof(int), cancellationToken);
                if (lengthBuffer is null)
                {
                    break;
                }

                var length = BitConverter.ToInt32(lengthBuffer.Value.Span);
                if (length <= 0)
                {
                    break;
                }

                var payloadBuffer = await ReadExactAsync(_stream, length, cancellationToken);
                if (payloadBuffer is null)
                {
                    break;
                }

                MessageReceived?.Invoke(this, new CollabTransportMessageEventArgs(payloadBuffer.Value));
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
            await DisconnectAsync();
        }
    }

    private static async Task<ReadOnlyMemory<byte>?> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        return buffer;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
    }
}

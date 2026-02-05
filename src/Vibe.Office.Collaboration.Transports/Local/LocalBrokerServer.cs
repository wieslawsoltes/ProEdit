using System.Net;
using System.Net.Sockets;
using Vibe.Office.Collaboration;

namespace Vibe.Office.Collaboration.Transports.Local;

public sealed class LocalBrokerServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly List<BrokerConnection> _connections = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;

    public LocalBrokerServer(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public async ValueTask StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        _listener.Stop();

        List<BrokerConnection> connections;
        lock (_sync)
        {
            connections = _connections.ToList();
            _connections.Clear();
        }

        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            var connection = new BrokerConnection(client, this);
            lock (_sync)
            {
                _connections.Add(connection);
            }

            _ = connection.ReadLoopAsync(cancellationToken);
        }
    }

    internal void RemoveConnection(BrokerConnection connection)
    {
        lock (_sync)
        {
            _connections.Remove(connection);
        }
    }

    internal async Task BroadcastAsync(BrokerConnection sender, ReadOnlyMemory<byte> payload)
    {
        List<BrokerConnection> connections;
        lock (_sync)
        {
            connections = _connections.ToList();
        }

        foreach (var connection in connections)
        {
            if (ReferenceEquals(connection, sender))
            {
                continue;
            }

            await connection.SendAsync(payload);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }

    internal sealed class BrokerConnection : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly LocalBrokerServer _server;
        private readonly object _sendSync = new();

        public BrokerConnection(TcpClient client, LocalBrokerServer server)
        {
            _client = client;
            _stream = client.GetStream();
            _server = server;
        }

        public async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var lengthBytes = await ReadExactAsync(sizeof(int), cancellationToken);
                    if (lengthBytes is null)
                    {
                        break;
                    }

                    var length = BitConverter.ToInt32(lengthBytes.Value.Span);
                    if (length <= 0)
                    {
                        break;
                    }

                    var payloadBytes = await ReadExactAsync(length, cancellationToken);
                    if (payloadBytes is null)
                    {
                        break;
                    }

                    await _server.BroadcastAsync(this, payloadBytes.Value);
                }
            }
            finally
            {
                _server.RemoveConnection(this);
                await DisposeAsync();
            }
        }

        public Task SendAsync(ReadOnlyMemory<byte> payload)
        {
            lock (_sendSync)
            {
                var lengthBytes = BitConverter.GetBytes(payload.Length);
                _stream.Write(lengthBytes, 0, lengthBytes.Length);
                _stream.Write(payload.Span);
                _stream.Flush();
            }

            return Task.CompletedTask;
        }

        private async Task<ReadOnlyMemory<byte>?> ReadExactAsync(int length, CancellationToken cancellationToken)
        {
            var buffer = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
                if (read == 0)
                {
                    return null;
                }

                offset += read;
            }

            return buffer;
        }

        public ValueTask DisposeAsync()
        {
            _stream.Dispose();
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

using ProEdit.Collaboration;

namespace ProEdit.Collaboration.Transports;

/// <summary>
/// Simple in-memory loopback transport for testing.
/// </summary>
public sealed class InMemoryLoopbackTransport : ICollabTransportConnection
{
    /// <summary>
    /// Raised when a payload is delivered to the loopback.
    /// </summary>
    public event EventHandler<CollabTransportMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Raised when the transport state changes.
    /// </summary>
    public event EventHandler<CollabTransportStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Sends a payload and immediately raises <see cref=\"MessageReceived\"/>.
    /// </summary>
    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        MessageReceived?.Invoke(this, new CollabTransportMessageEventArgs(payload));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Connects the loopback transport.
    /// </summary>
    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(CollabTransportState.Connected);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Disconnects the loopback transport.
    /// </summary>
    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(CollabTransportState.Disconnected);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Updates the transport state.
    /// </summary>
    public void SetState(CollabTransportState state, string? message = null)
    {
        StateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(state, message));
    }
}

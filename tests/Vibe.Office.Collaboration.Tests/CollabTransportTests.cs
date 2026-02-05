using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Transports;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class CollabTransportTests
{
    [Fact]
    public async Task LoopbackTransportReportsConnectAndDisconnect()
    {
        var transport = new InMemoryLoopbackTransport();
        var states = new List<CollabTransportState>();
        transport.StateChanged += (_, args) => states.Add(args.State);

        await transport.ConnectAsync();
        await transport.DisconnectAsync();

        Assert.Contains(CollabTransportState.Connected, states);
        Assert.Contains(CollabTransportState.Disconnected, states);
    }
}

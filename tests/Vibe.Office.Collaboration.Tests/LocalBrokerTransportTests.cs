using System.Net;
using System.Text;
using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Transports.Local;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class LocalBrokerTransportTests
{
    [Fact]
    public async Task LocalBroker_RelaysMessagesBetweenClients()
    {
        await using var server = new LocalBrokerServer(0);
        server.Start();
        var port = server.Port;

        await using var clientA = new LocalBrokerClientTransport(IPAddress.Loopback.ToString(), port);
        await using var clientB = new LocalBrokerClientTransport(IPAddress.Loopback.ToString(), port);

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientB.MessageReceived += (_, args) =>
        {
            received.TrySetResult(Encoding.UTF8.GetString(args.Payload.Span));
        };

        await clientA.ConnectAsync();
        await clientB.ConnectAsync();
        await Task.Delay(100);

        var payload = Encoding.UTF8.GetBytes("hello");
        await clientA.SendAsync(payload);

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("hello", result);

        await clientA.DisconnectAsync();
        await clientB.DisconnectAsync();
    }
}

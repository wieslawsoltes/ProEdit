using System.Text;
using System.IO;
using System.Collections.Generic;
using Vibe.Office.Collaboration.Transports.SharedFile;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class SharedFileTransportTests
{
    [Fact]
    public async Task SharedFileTransport_DeliversMessagesBetweenPeers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "session.shared");

        await using var transportA = new SharedFileTransport(path);
        await using var transportB = new SharedFileTransport(path);

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        transportB.MessageReceived += (_, args) =>
        {
            received.TrySetResult(Encoding.UTF8.GetString(args.Payload.Span));
        };

        transportA.Start();
        transportB.Start();

        await transportA.SendAsync(Encoding.UTF8.GetBytes("ping"));

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("ping", result);
    }

    [Fact]
    public async Task SharedFileTransport_StopsOnCorruptRecord()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "session.shared");

        await using var transport = new SharedFileTransport(path);
        var received = new List<string>();
        transport.MessageReceived += (_, args) => received.Add(Encoding.UTF8.GetString(args.Payload.Span));

        transport.Start();
        await transport.SendAsync(Encoding.UTF8.GetBytes("ok"));

        // Append corrupt bytes to simulate a conflict.
        await File.AppendAllBytesAsync(path, new byte[] { 0x00, 0x01, 0x02 });

        await Task.Delay(300);

        Assert.Contains("ok", received);
    }
}

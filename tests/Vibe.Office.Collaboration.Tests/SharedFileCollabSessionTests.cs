using System.IO;
using System.Threading.Tasks;
using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Persistence;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.Collaboration.Transports.SharedFile;
using Vibe.Office.Documents;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class SharedFileCollabSessionTests
{
    [Fact]
    public async Task SnapshotUpdatesAreAppliedBetweenSharedFileSessions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "session");

        var transportOptions = new SharedFileTransportOptions(TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(2));
        var optionsA = new CollabRealtimeSessionOptions
        {
            DocumentId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            SenderId = Guid.NewGuid()
        };

        var optionsB = new CollabRealtimeSessionOptions
        {
            DocumentId = optionsA.DocumentId,
            SessionId = Guid.NewGuid(),
            SenderId = Guid.NewGuid()
        };

        await using var sessionA = new SharedFileCollabSession(basePath, optionsA, transportOptions);
        await using var sessionB = new SharedFileCollabSession(basePath, optionsB, transportOptions);

        var snapshotReceived = new TaskCompletionSource<CollabSnapshotReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionB.SnapshotReceived += (_, args) =>
        {
            if (args.Snapshot.Version >= 1)
            {
                snapshotReceived.TrySetResult(args);
            }
        };

        await sessionB.ConnectAsync();
        await sessionA.ConnectAsync();

        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Snapshot update"));
        var snapshot = CollabSnapshot.Create(1, document);
        var serializer = new CollabSnapshotSerializer();
        var payload = serializer.Serialize(snapshot);

        await sessionA.SendSnapshotAsync(new SnapshotMessage(snapshot.SnapshotId, snapshot.Version, payload));

        var result = await snapshotReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, result.Snapshot.Version);
        Assert.NotEmpty(result.Payload.ToArray());
    }
}

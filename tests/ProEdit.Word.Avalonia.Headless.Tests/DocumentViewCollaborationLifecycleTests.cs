using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ProEdit.Collaboration;
using ProEdit.Collaboration.Protocol;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Word.Avalonia;
using Xunit;

namespace ProEdit.Word.Avalonia.Headless.Tests;

public sealed class DocumentViewCollaborationLifecycleTests
{
    [AvaloniaFact]
    public async Task CollaborationSession_RemainsActive_AfterLoadDocument()
    {
        var view = new DocumentView();
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();

        try
        {
            await WaitForUiAsync();

            var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            view.EnableCollaboration(session);

            view.LoadDocument(CreateDocument("Loaded"));

            var paragraph = view.Document.GetParagraph(0);
            var offset = DocumentEditHelpers.GetParagraphLength(paragraph);
            session.RaiseOpsReceived(CreateInsertBatch(paragraph.NodeId, offset, "X"));

            await WaitUntilAsync(() => GetParagraphText(view) == "LoadedX");
            Assert.Equal("LoadedX", GetParagraphText(view));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CollaborationSession_RemainsActive_AfterLoadDocumentAsync()
    {
        var view = new DocumentView();
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();

        try
        {
            await WaitForUiAsync();

            var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            view.EnableCollaboration(session);

            await view.LoadDocumentAsync(CreateDocument("Async"));

            var paragraph = view.Document.GetParagraph(0);
            var offset = DocumentEditHelpers.GetParagraphLength(paragraph);
            session.RaiseOpsReceived(CreateInsertBatch(paragraph.NodeId, offset, "Y"));

            await WaitUntilAsync(() => GetParagraphText(view) == "AsyncY");
            Assert.Equal("AsyncY", GetParagraphText(view));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CollaborationSession_Detaches_WhenViewLeavesVisualTree()
    {
        var view = new DocumentView();
        view.LoadDocument(CreateDocument("Stable"));
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();

        try
        {
            await WaitForUiAsync();

            var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            view.EnableCollaboration(session);

            var before = GetParagraphText(view);
            var paragraph = view.Document.GetParagraph(0);
            var offset = DocumentEditHelpers.GetParagraphLength(paragraph);

            window.Content = null;
            await WaitForUiAsync();

            session.RaiseOpsReceived(CreateInsertBatch(paragraph.NodeId, offset, "Z"));
            await WaitForUiAsync();
            await Task.Delay(40);
            await WaitForUiAsync();

            Assert.Equal(before, GetParagraphText(view));
        }
        finally
        {
            window.Close();
        }
    }

    private static Document CreateDocument(string text)
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock(text));
        return document;
    }

    private static string GetParagraphText(DocumentView view)
    {
        return DocumentEditHelpers.GetParagraphText(view.Document.GetParagraph(0));
    }

    private static CollabOpBatch CreateInsertBatch(Guid paragraphNodeId, int offset, string text)
    {
        return CollabOpBatch.Create(
            actorId: Guid.NewGuid(),
            baseVersion: 0,
            sequence: 1,
            lamport: 1,
            ops: new ICollabOp[]
            {
                new InsertTextOp(TextAnchor.Before(paragraphNodeId, offset), text)
            });
    }

    private static async Task WaitForUiAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { });
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var started = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - started > timeoutMs)
            {
                break;
            }

            await WaitForUiAsync();
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for collaboration state to converge.");
    }

    private sealed class RecordingRealtimeSession : ICollabRealtimeSession
    {
        public RecordingRealtimeSession(Guid documentId, Guid sessionId, Guid senderId)
        {
            DocumentId = documentId;
            SessionId = sessionId;
            SenderId = senderId;
        }

        public Guid DocumentId { get; }

        public Guid SessionId { get; }

        public Guid SenderId { get; }

        public event EventHandler<CollabTransportStateChangedEventArgs>? TransportStateChanged;
        public event EventHandler<CollabOpsReceivedEventArgs>? OpsReceived;
        public event EventHandler<CollabSnapshotReceivedEventArgs>? SnapshotReceived;
        public event EventHandler<CollabPresenceEventArgs>? PresenceReceived;
        public event EventHandler<CollabErrorEventArgs>? ErrorReceived;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            TransportStateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Connected));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            TransportStateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Disconnected));
            return ValueTask.CompletedTask;
        }

        public ValueTask SubmitLocalAsync(CollabOpBatch batch, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPresenceAsync(
            PresenceState presence,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            PresenceReceived?.Invoke(
                this,
                new CollabPresenceEventArgs(
                    presence,
                    timeToLive ?? TimeSpan.FromSeconds(10),
                    DateTimeOffset.UtcNow));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendSnapshotAsync(SnapshotMessage snapshot, CancellationToken cancellationToken = default)
        {
            SnapshotReceived?.Invoke(this, new CollabSnapshotReceivedEventArgs(snapshot, DateTimeOffset.UtcNow));
            return ValueTask.CompletedTask;
        }

        public void RaiseOpsReceived(CollabOpBatch batch)
        {
            OpsReceived?.Invoke(this, new CollabOpsReceivedEventArgs(batch, DateTimeOffset.UtcNow));
        }

        public void RaiseError(ErrorMessage error)
        {
            ErrorReceived?.Invoke(this, new CollabErrorEventArgs(error, DateTimeOffset.UtcNow));
        }
    }
}

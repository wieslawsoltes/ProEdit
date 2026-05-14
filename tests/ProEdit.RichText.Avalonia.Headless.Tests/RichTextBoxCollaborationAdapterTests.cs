using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ProEdit.Collaboration;
using ProEdit.Collaboration.Protocol;
using ProEdit.Collaboration.UI;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.RichText.Avalonia;
using Xunit;

namespace ProEdit.RichText.Avalonia.Headless.Tests;

public sealed class RichTextBoxCollaborationAdapterTests
{
    [Fact]
    public void ApiSurface_DoesNotExposeCollaborationMembers()
    {
        var type = typeof(RichTextBox);
        var flags = BindingFlags.Instance | BindingFlags.Public;

        Assert.Null(type.GetMethod("EnableCollaboration", flags));
        Assert.Null(type.GetMethod("DisableCollaboration", flags));
        Assert.Null(type.GetMethod("RegisterService", flags));
        Assert.Null(type.GetMethod("TryGetService", flags));
        Assert.Null(type.GetEvent("EditorSessionRebuilt", flags));
    }

    [AvaloniaFact]
    public void InternalAdapter_RegistersAndResolvesServices()
    {
        var box = new RichTextBox();
        var adapter = (IRichTextBoxCollaborationAdapter)box;
        var identity = new DefaultCollabIdentityService();
        var state = new CollabUiState(identity);

        try
        {
            adapter.RegisterService<ICollabIdentityService>(identity);
            adapter.RegisterService<ICollabUiService>(state);

            Assert.True(adapter.TryGetService<ICollabIdentityService>(out var resolvedIdentity));
            Assert.Same(identity, resolvedIdentity);
            Assert.True(adapter.TryGetService<ICollabUiService>(out var resolvedUiState));
            Assert.Same(state, resolvedUiState);
        }
        finally
        {
            state.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public async Task InternalAdapter_AttachSession_AppliesRemoteTextInsert()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };

        var adapter = (IRichTextBoxCollaborationAdapter)box;
        var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        adapter.AttachSession(session, authorResolver: null);

        var paragraph = box.EditorDocumentForTests.GetParagraph(0);
        var batch = CollabOpBatch.Create(
            actorId: Guid.NewGuid(),
            baseVersion: 0,
            sequence: 1,
            lamport: 1,
            ops: new ICollabOp[]
            {
                new InsertTextOp(TextAnchor.Before(paragraph.NodeId, 5), "X")
            });
        session.RaiseOpsReceived(batch);

        await WaitUntilAsync(() => GetEditorParagraphText(box, 0) == "AlphaX");

        Assert.Equal("AlphaX", GetEditorParagraphText(box, 0));
        var flowParagraph = Assert.IsType<ProEdit.FlowDocument.Paragraph>(box.Document.Blocks[0]);
        var flowRun = Assert.IsType<ProEdit.FlowDocument.Run>(flowParagraph.Inlines[0]);
        Assert.Equal("AlphaX", flowRun.Text);
    }

    [AvaloniaFact]
    public void InternalAdapter_ExposesEditorRebuildEvent()
    {
        var box = new RichTextBox();
        var adapter = (IRichTextBoxCollaborationAdapter)box;
        var raised = 0;
        adapter.EditorSessionRebuilt += OnEditorSessionRebuilt;

        try
        {
            box.Document = BuildFlowDocument("Rebuilt");
            Assert.True(raised >= 1);
        }
        finally
        {
            adapter.EditorSessionRebuilt -= OnEditorSessionRebuilt;
        }

        void OnEditorSessionRebuilt(object? sender, EventArgs e)
        {
            raised++;
        }
    }

    private static ProEdit.FlowDocument.FlowDocument BuildFlowDocument(string text)
    {
        var document = new ProEdit.FlowDocument.FlowDocument();
        document.Blocks.Add(new ProEdit.FlowDocument.Paragraph(text));
        return document;
    }

    private static string GetEditorParagraphText(RichTextBox box, int paragraphIndex)
    {
        return DocumentEditHelpers.GetParagraphText(box.EditorDocumentForTests.GetParagraph(paragraphIndex));
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

            await Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for collaboration update.");
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

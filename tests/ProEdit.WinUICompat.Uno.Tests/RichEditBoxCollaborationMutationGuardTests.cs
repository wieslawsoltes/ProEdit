using ProEdit.Collaboration;
using ProEdit.Collaboration.Protocol;
using ProEdit.WinUICompat.Controls;
using ProEdit.WinUICompat.Documents;
using Xunit;

namespace ProEdit.WinUICompat.Uno.Tests;

public sealed class RichEditBoxCollaborationMutationGuardTests
{
    [Fact]
    public void ExternalCompatBlockMutation_Throws_WhenCollaborationAttached()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();
        var adapter = (IRichEditBoxCollaborationAdapter)box;
        adapter.AttachSession(new FakeRealtimeSession(), authorResolver: null);

        Assert.Throws<InvalidOperationException>(() =>
            box.Document.Document.Blocks.Add(new Paragraph("external mutation")));
    }

    [Fact]
    public void ExternalCompatBlockMutation_DoesNotThrow_AfterDetach()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();
        var adapter = (IRichEditBoxCollaborationAdapter)box;
        adapter.AttachSession(new FakeRealtimeSession(), authorResolver: null);
        adapter.DetachSession();

        var exception = Record.Exception(() =>
            box.Document.Document.Blocks.Add(new Paragraph("external mutation")));
        Assert.Null(exception);
    }

    private sealed class FakeRealtimeSession : ICollabRealtimeSession
    {
        public Guid DocumentId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid SenderId { get; } = Guid.NewGuid();

        public event EventHandler<CollabTransportStateChangedEventArgs>? TransportStateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<CollabOpsReceivedEventArgs>? OpsReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<CollabSnapshotReceivedEventArgs>? SnapshotReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<CollabPresenceEventArgs>? PresenceReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<CollabErrorEventArgs>? ErrorReceived
        {
            add { }
            remove { }
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask SubmitLocalAsync(CollabOpBatch batch, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask SendPresenceAsync(PresenceState presence, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask SendSnapshotAsync(SnapshotMessage snapshot, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}

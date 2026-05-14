using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ProEdit.Collaboration;
using ProEdit.Collaboration.Protocol;
using ProEdit.Collaboration.UI;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.RichText.Avalonia;
using ProEdit.RichText.Avalonia.Collaboration;
using Xunit;

namespace ProEdit.RichText.Avalonia.Headless.Tests;

public sealed class RichTextBoxCollaborationBehaviorTests
{
    [AvaloniaFact]
    public async Task Behavior_AttachesSession_WhenEnabled()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };
        var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        RichTextBoxCollaborationBehavior.SetSession(box, session);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

        var paragraph = box.EditorDocumentForTests.GetParagraph(0);
        var batch = CreateInsertBatch(paragraph.NodeId, 5, "X");
        session.RaiseOpsReceived(batch);

        await WaitUntilAsync(() => GetEditorParagraphText(box, 0) == "AlphaX");
        Assert.Equal("AlphaX", GetEditorParagraphText(box, 0));
    }

    [AvaloniaFact]
    public async Task Behavior_DetachesSession_WhenDisabled()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };
        var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        RichTextBoxCollaborationBehavior.SetSession(box, session);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

        var paragraph = box.EditorDocumentForTests.GetParagraph(0);
        session.RaiseOpsReceived(CreateInsertBatch(paragraph.NodeId, 5, "X"));
        await WaitUntilAsync(() => GetEditorParagraphText(box, 0) == "AlphaX");

        RichTextBoxCollaborationBehavior.SetIsEnabled(box, false);
        await WaitForUiAsync();

        // The previous operation changed paragraph text length by one.
        session.RaiseOpsReceived(CreateInsertBatch(paragraph.NodeId, 6, "Y"));
        await WaitForUiAsync();
        await Task.Delay(40);
        await WaitForUiAsync();

        Assert.Equal("AlphaX", GetEditorParagraphText(box, 0));
    }

    [AvaloniaFact]
    public void Behavior_RegistersServices_WithRichTextAdapter()
    {
        var box = new RichTextBox();
        var identity = new DefaultCollabIdentityService();
        var uiState = new CollabUiState(identity);

        try
        {
            RichTextBoxCollaborationBehavior.SetCollabIdentityService(box, identity);
            RichTextBoxCollaborationBehavior.SetCollabUiService(box, uiState);
            RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

            var adapter = (IRichTextBoxCollaborationAdapter)box;
            Assert.True(adapter.TryGetService<ICollabIdentityService>(out var resolvedIdentity));
            Assert.Same(identity, resolvedIdentity);
            Assert.True(adapter.TryGetService<ICollabUiService>(out var resolvedUiState));
            Assert.Same(uiState, resolvedUiState);
        }
        finally
        {
            uiState.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public async Task Behavior_ReappliesSession_AfterEditorRebuild()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Old")
        };
        var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        RichTextBoxCollaborationBehavior.SetSession(box, session);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

        box.Document = BuildFlowDocument("New");

        var paragraph = box.EditorDocumentForTests.GetParagraph(0);
        session.RaiseOpsReceived(CreateInsertBatch(paragraph.NodeId, 3, "Z"));
        await WaitUntilAsync(() => GetEditorParagraphText(box, 0) == "NewZ");
        Assert.Equal("NewZ", GetEditorParagraphText(box, 0));
    }

    [AvaloniaFact]
    public async Task Behavior_ReRegistersServices_AfterEditorRebuild()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };
        var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var identity = new DefaultCollabIdentityService();
        var uiService = new StubCollabUiService();

        RichTextBoxCollaborationBehavior.SetSession(box, session);
        RichTextBoxCollaborationBehavior.SetCollabIdentityService(box, identity);
        RichTextBoxCollaborationBehavior.SetCollabUiService(box, uiService);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

        var adapter = (IRichTextBoxCollaborationAdapter)box;
        Assert.True(adapter.TryGetService<ICollabIdentityService>(out _));
        Assert.True(adapter.TryGetService<ICollabUiService>(out _));

        box.Document = BuildFlowDocument("Reloaded");
        await WaitForUiAsync();

        Assert.True(adapter.TryGetService<ICollabIdentityService>(out var resolvedIdentity));
        Assert.Same(identity, resolvedIdentity);
        Assert.True(adapter.TryGetService<ICollabUiService>(out var resolvedUiService));
        Assert.Same(uiService, resolvedUiService);
    }

    [AvaloniaFact]
    public void Behavior_RejectsExternalFlowMutation_WhenCollaborationActive()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };
        var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        RichTextBoxCollaborationBehavior.SetSession(box, session);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            box.Document.Blocks.Add(new ProEdit.FlowDocument.Paragraph("External"));
        });

        Assert.Contains("External FlowDocument mutations are not supported", error.Message, StringComparison.Ordinal);
        Assert.Equal("Alpha", GetEditorParagraphText(box, 0));
    }

    [AvaloniaFact]
    public async Task Behavior_AllowsReloadScenario_AfterMutationConflict()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Source")
        };
        var session = new RecordingRealtimeSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        RichTextBoxCollaborationBehavior.SetSession(box, session);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

        Assert.Throws<InvalidOperationException>(() =>
        {
            box.Document.Blocks[0] = new ProEdit.FlowDocument.Paragraph("Conflict");
        });

        box.Document = BuildFlowDocument("Reload");
        var paragraph = box.EditorDocumentForTests.GetParagraph(0);
        session.RaiseOpsReceived(CreateInsertBatch(paragraph.NodeId, 6, "Q"));

        await WaitUntilAsync(() => GetEditorParagraphText(box, 0) == "ReloadQ");
        Assert.Equal("ReloadQ", GetEditorParagraphText(box, 0));
    }

    [AvaloniaFact]
    public async Task Behavior_UpdatesDiagnostics_FromUiServiceState()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };
        var uiService = new StubCollabUiService();
        var identity = new DefaultCollabIdentityService();
        var remoteA = new CollabParticipant(Guid.NewGuid(), "Remote A", "#1C5F92", DateTimeOffset.UtcNow, false);
        var remoteB = new CollabParticipant(Guid.NewGuid(), "Remote B", "#6A3D9A", DateTimeOffset.UtcNow, false);

        RichTextBoxCollaborationBehavior.SetCollabIdentityService(box, identity);
        RichTextBoxCollaborationBehavior.SetCollabUiService(box, uiService);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

        uiService.SetConnectionState(CollabConnectionState.Connected, "Connected");
        uiService.UpdateDiagnostics(TimeSpan.FromMilliseconds(125), 4, TimeSpan.FromSeconds(8));
        uiService.UpdateParticipants(new[] { remoteA, remoteB });
        uiService.UpdatePresence(
            new PresenceState(remoteA.UserId, remoteA.DisplayName, null, null, DateTimeOffset.UtcNow, remoteA.Color),
            TimeSpan.FromSeconds(10));
        uiService.UpdatePresence(
            new PresenceState(remoteB.UserId, remoteB.DisplayName, null, null, DateTimeOffset.UtcNow, remoteB.Color),
            TimeSpan.FromSeconds(10));

        await WaitUntilAsync(() =>
            RichTextBoxCollaborationBehavior.GetConnectionState(box) == CollabConnectionState.Connected
            && RichTextBoxCollaborationBehavior.GetConnectionMessage(box) == "Connected"
            && RichTextBoxCollaborationBehavior.GetSyncLag(box) == TimeSpan.FromMilliseconds(125)
            && RichTextBoxCollaborationBehavior.GetOpQueueDepth(box) == 4
            && RichTextBoxCollaborationBehavior.GetSnapshotAge(box) == TimeSpan.FromSeconds(8)
            && RichTextBoxCollaborationBehavior.GetParticipantCount(box) == 2
            && RichTextBoxCollaborationBehavior.GetPresenceCount(box) == 2);
    }

    [AvaloniaFact]
    public async Task Behavior_ResetsAndResubscribesDiagnostics_WhenToggled()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };
        var uiService = new StubCollabUiService();
        var identity = new DefaultCollabIdentityService();
        var remote = new CollabParticipant(Guid.NewGuid(), "Remote", "#236B2A", DateTimeOffset.UtcNow, false);

        RichTextBoxCollaborationBehavior.SetCollabIdentityService(box, identity);
        RichTextBoxCollaborationBehavior.SetCollabUiService(box, uiService);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

        uiService.SetConnectionState(CollabConnectionState.Connected, "Initial");
        uiService.UpdateDiagnostics(TimeSpan.FromMilliseconds(80), 2, TimeSpan.FromSeconds(5));
        uiService.UpdateParticipants(new[] { remote });
        uiService.UpdatePresence(
            new PresenceState(remote.UserId, remote.DisplayName, null, null, DateTimeOffset.UtcNow, remote.Color),
            TimeSpan.FromSeconds(10));

        await WaitUntilAsync(() =>
            RichTextBoxCollaborationBehavior.GetConnectionState(box) == CollabConnectionState.Connected
            && RichTextBoxCollaborationBehavior.GetParticipantCount(box) == 1
            && RichTextBoxCollaborationBehavior.GetPresenceCount(box) == 1);

        var adapter = (IRichTextBoxCollaborationAdapter)box;
        Assert.True(adapter.TryGetService<ICollabIdentityService>(out _));
        Assert.True(adapter.TryGetService<ICollabUiService>(out _));

        RichTextBoxCollaborationBehavior.SetIsEnabled(box, false);
        await WaitUntilAsync(() =>
            RichTextBoxCollaborationBehavior.GetConnectionState(box) == CollabConnectionState.Disconnected
            && RichTextBoxCollaborationBehavior.GetConnectionMessage(box) is null
            && RichTextBoxCollaborationBehavior.GetSyncLag(box) == TimeSpan.Zero
            && RichTextBoxCollaborationBehavior.GetOpQueueDepth(box) == 0
            && RichTextBoxCollaborationBehavior.GetSnapshotAge(box) == TimeSpan.Zero
            && RichTextBoxCollaborationBehavior.GetParticipantCount(box) == 0
            && RichTextBoxCollaborationBehavior.GetPresenceCount(box) == 0);

        Assert.False(adapter.TryGetService<ICollabIdentityService>(out _));
        Assert.False(adapter.TryGetService<ICollabUiService>(out _));

        uiService.SetConnectionState(CollabConnectionState.Reconnecting, "Retry");
        uiService.UpdateDiagnostics(TimeSpan.FromMilliseconds(250), 6, TimeSpan.FromSeconds(9));
        uiService.ClearPresence();

        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);
        await WaitUntilAsync(() =>
            RichTextBoxCollaborationBehavior.GetConnectionState(box) == CollabConnectionState.Reconnecting
            && RichTextBoxCollaborationBehavior.GetConnectionMessage(box) == "Retry"
            && RichTextBoxCollaborationBehavior.GetOpQueueDepth(box) == 6
            && RichTextBoxCollaborationBehavior.GetPresenceCount(box) == 0);

        Assert.True(adapter.TryGetService<ICollabIdentityService>(out _));
        Assert.True(adapter.TryGetService<ICollabUiService>(out _));

        uiService.SetConnectionState(CollabConnectionState.Connected, "Recovered");
        uiService.UpdateDiagnostics(TimeSpan.FromMilliseconds(20), 1, TimeSpan.FromMilliseconds(75));
        await WaitUntilAsync(() =>
            RichTextBoxCollaborationBehavior.GetConnectionState(box) == CollabConnectionState.Connected
            && RichTextBoxCollaborationBehavior.GetConnectionMessage(box) == "Recovered"
            && RichTextBoxCollaborationBehavior.GetOpQueueDepth(box) == 1);
    }

    [AvaloniaFact]
    public async Task Behavior_ResetsDiagnostics_WhenUiServiceCleared()
    {
        var box = new RichTextBox
        {
            Document = BuildFlowDocument("Alpha")
        };
        var uiService = new StubCollabUiService();
        var identity = new DefaultCollabIdentityService();

        RichTextBoxCollaborationBehavior.SetCollabIdentityService(box, identity);
        RichTextBoxCollaborationBehavior.SetCollabUiService(box, uiService);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);

        uiService.SetConnectionState(CollabConnectionState.Connected, "Connected");
        uiService.UpdateDiagnostics(TimeSpan.FromMilliseconds(64), 3, TimeSpan.FromSeconds(4));
        await WaitUntilAsync(() => RichTextBoxCollaborationBehavior.GetConnectionState(box) == CollabConnectionState.Connected);

        var adapter = (IRichTextBoxCollaborationAdapter)box;
        Assert.True(adapter.TryGetService<ICollabUiService>(out _));

        RichTextBoxCollaborationBehavior.SetCollabUiService(box, null);
        await WaitUntilAsync(() =>
            RichTextBoxCollaborationBehavior.GetConnectionState(box) == CollabConnectionState.Disconnected
            && RichTextBoxCollaborationBehavior.GetConnectionMessage(box) is null
            && RichTextBoxCollaborationBehavior.GetSyncLag(box) == TimeSpan.Zero
            && RichTextBoxCollaborationBehavior.GetOpQueueDepth(box) == 0
            && RichTextBoxCollaborationBehavior.GetSnapshotAge(box) == TimeSpan.Zero
            && RichTextBoxCollaborationBehavior.GetParticipantCount(box) == 0
            && RichTextBoxCollaborationBehavior.GetPresenceCount(box) == 0);

        Assert.False(adapter.TryGetService<ICollabUiService>(out _));
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

        Assert.True(condition(), "Timed out waiting for collaboration update.");
    }

    private sealed class StubCollabUiService : ICollabUiService
    {
        private readonly Dictionary<Guid, PresenceState> _presenceByUser = new();
        private IReadOnlyList<CollabParticipant> _participants = Array.Empty<CollabParticipant>();
        private IReadOnlyList<PresenceState> _presence = Array.Empty<PresenceState>();

        public CollabConnectionState ConnectionState { get; private set; } = CollabConnectionState.Disconnected;

        public string? ConnectionMessage { get; private set; }

        public Guid DocumentId { get; } = Guid.NewGuid();

        public Guid SessionId { get; } = Guid.NewGuid();

        public CollabTransportMode TransportMode { get; set; }

        public string? ServerUrl { get; set; }

        public string? SharedPath { get; set; }

        public string? ResolvedSharedPath => SharedPath;

        public int? LocalBrokerPort { get; set; }

        public IReadOnlyList<CollabParticipant> Participants => _participants;

        public IReadOnlyList<PresenceState> Presence => _presence;

        public TimeSpan SyncLag { get; private set; }

        public int OpQueueDepth { get; private set; }

        public TimeSpan SnapshotAge { get; private set; }

        public event EventHandler? StateChanged;

        public ValueTask JoinAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ShareAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public void UpdatePresence(PresenceState presence, TimeSpan? timeToLive = null)
        {
            _presenceByUser[presence.UserId] = presence;
            SnapshotPresence();
            RaiseStateChanged();
        }

        public void UpdateParticipants(IReadOnlyList<CollabParticipant> participants)
        {
            var snapshot = new CollabParticipant[participants.Count];
            for (var i = 0; i < participants.Count; i++)
            {
                snapshot[i] = participants[i];
            }

            _participants = snapshot;
            RaiseStateChanged();
        }

        public void UpdateDiagnostics(TimeSpan syncLag, int opQueueDepth, TimeSpan snapshotAge)
        {
            SyncLag = syncLag;
            OpQueueDepth = opQueueDepth;
            SnapshotAge = snapshotAge;
            RaiseStateChanged();
        }

        public void SetConnectionState(CollabConnectionState state, string? message = null)
        {
            ConnectionState = state;
            ConnectionMessage = message;
            RaiseStateChanged();
        }

        public void ClearError()
        {
            ConnectionMessage = null;
            if (ConnectionState == CollabConnectionState.Error)
            {
                ConnectionState = CollabConnectionState.Disconnected;
            }

            RaiseStateChanged();
        }

        public void ClearPresence()
        {
            _presenceByUser.Clear();
            _presence = Array.Empty<PresenceState>();
            RaiseStateChanged();
        }

        private void SnapshotPresence()
        {
            if (_presenceByUser.Count == 0)
            {
                _presence = Array.Empty<PresenceState>();
                return;
            }

            var snapshot = new PresenceState[_presenceByUser.Count];
            var index = 0;
            foreach (var item in _presenceByUser.Values)
            {
                snapshot[index++] = item;
            }

            _presence = snapshot;
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
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

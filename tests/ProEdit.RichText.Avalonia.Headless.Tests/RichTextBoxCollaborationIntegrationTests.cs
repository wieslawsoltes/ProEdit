using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using ProEdit.Collaboration;
using ProEdit.Collaboration.Protocol;
using ProEdit.Collaboration.UI;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.RichText.Avalonia;
using ProEdit.RichText.Avalonia.Collaboration;
using Xunit;
using FlowDocumentModel = ProEdit.FlowDocument.FlowDocument;
using FlowParagraph = ProEdit.FlowDocument.Paragraph;

namespace ProEdit.RichText.Avalonia.Headless.Tests;

public sealed class RichTextBoxCollaborationIntegrationTests
{
    [AvaloniaFact]
    public async Task SharedSession_RemoteTypingAndUndoRedo_StayInSync()
    {
        var context = await CreatePairAsync("Alpha");
        try
        {
            context.BoxA.CaretPosition = new FlowTextPointer(context.BoxA.Document, 0, 5);
            await RaiseTextInputAsync(context.BoxA, "!");
            await WaitUntilAsync(() =>
                GetEditorParagraphText(context.BoxA, 0) == "Alpha!"
                && GetEditorParagraphText(context.BoxB, 0) == "Alpha!");

            Assert.True(context.BoxA.Undo());
            await WaitUntilAsync(() =>
                GetEditorParagraphText(context.BoxA, 0) == "Alpha"
                && GetEditorParagraphText(context.BoxB, 0) == "Alpha");

            Assert.True(context.BoxA.Redo());
            await WaitUntilAsync(() =>
                GetEditorParagraphText(context.BoxA, 0) == "Alpha!"
                && GetEditorParagraphText(context.BoxB, 0) == "Alpha!");

            Assert.True(context.SessionA.SubmittedBatchCount > 0);
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task SharedSession_PropagatesSelectionPresence()
    {
        var context = await CreatePairAsync("Alpha Beta");
        try
        {
            context.BoxA.Selection.Select(
                new FlowTextPointer(context.BoxA.Document, 0, 0),
                new FlowTextPointer(context.BoxA.Document, 0, 5));

            await WaitUntilAsync(() =>
                context.UiServiceB.TryGetPresence(context.IdentityA.UserId, out var remote)
                && remote.HasSelection);

            Assert.True(context.UiServiceB.TryGetPresence(context.IdentityA.UserId, out var presence));
            Assert.True(presence.HasSelection);
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task SharedSession_DocumentSwapWhileConnected_RetainsCollaboration()
    {
        var context = await CreatePairAsync("Base");
        try
        {
            context.BoxA.Document = BuildFlowDocument("Swap");
            context.BoxB.Document = BuildFlowDocument("Swap");
            await WaitForUiAsync();

            // Keep both editors on an identical node graph so anchored remote ops remain valid.
            AlignEditorDocument(context.BoxA, context.BoxB);
            await WaitForUiAsync();

            context.BoxA.CaretPosition = new FlowTextPointer(context.BoxA.Document, 0, 4);
            await RaiseTextInputAsync(context.BoxA, "?");

            await WaitUntilAsync(() =>
                GetEditorParagraphText(context.BoxA, 0) == "Swap?"
                && GetEditorParagraphText(context.BoxB, 0) == "Swap?");
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private static async Task<CollaborationPairContext> CreatePairAsync(string initialText)
    {
        var boxA = new RichTextBox
        {
            Document = BuildFlowDocument(initialText)
        };
        var boxB = new RichTextBox
        {
            Document = BuildFlowDocument(initialText)
        };

        var (window, style) = await ShowInWindowAsync(boxA, boxB);
        AlignEditorDocument(boxA, boxB);
        await WaitForUiAsync();

        var hub = new SharedRealtimeHub();
        var documentId = Guid.NewGuid();
        var identityA = new FixedIdentityService(Guid.NewGuid(), "User A", "#2F5DA8");
        var identityB = new FixedIdentityService(Guid.NewGuid(), "User B", "#4E8A3A");
        var sessionA = new HubRealtimeSession(hub, documentId, Guid.NewGuid(), identityA.UserId);
        var sessionB = new HubRealtimeSession(hub, documentId, Guid.NewGuid(), identityB.UserId);
        var uiServiceA = new SessionBackedCollabUiService(sessionA, identityA);
        var uiServiceB = new SessionBackedCollabUiService(sessionB, identityB);

        var authorResolver = CreateAuthorResolver(identityA, identityB);
        AttachBehavior(boxA, sessionA, uiServiceA, identityA, authorResolver);
        AttachBehavior(boxB, sessionB, uiServiceB, identityB, authorResolver);

        await sessionA.ConnectAsync();
        await sessionB.ConnectAsync();
        await WaitUntilAsync(() =>
            uiServiceA.ConnectionState == CollabConnectionState.Connected
            && uiServiceB.ConnectionState == CollabConnectionState.Connected);

        return new CollaborationPairContext(
            boxA,
            boxB,
            sessionA,
            sessionB,
            uiServiceA,
            uiServiceB,
            identityA,
            window,
            style);
    }

    private static void AttachBehavior(
        RichTextBox box,
        ICollabRealtimeSession session,
        ICollabUiService uiService,
        ICollabIdentityService identityService,
        Func<Guid, string?> authorResolver)
    {
        RichTextBoxCollaborationBehavior.SetSession(box, session);
        RichTextBoxCollaborationBehavior.SetCollabUiService(box, uiService);
        RichTextBoxCollaborationBehavior.SetCollabIdentityService(box, identityService);
        RichTextBoxCollaborationBehavior.SetAuthorResolver(box, authorResolver);
        RichTextBoxCollaborationBehavior.SetIsEnabled(box, true);
    }

    private static Func<Guid, string?> CreateAuthorResolver(
        ICollabIdentityService identityA,
        ICollabIdentityService identityB)
    {
        return authorId =>
        {
            if (authorId == identityA.UserId)
            {
                return identityA.DisplayName;
            }

            if (authorId == identityB.UserId)
            {
                return identityB.DisplayName;
            }

            return null;
        };
    }

    private static void AlignEditorDocument(RichTextBox source, RichTextBox target)
    {
        var clone = DocumentClone.Clone(source.EditorDocumentForTests);
        target.ReplaceEditorDocumentForTests(clone);
    }

    private static FlowDocumentModel BuildFlowDocument(string text)
    {
        var document = new FlowDocumentModel();
        document.Blocks.Clear();
        document.Blocks.Add(new FlowParagraph(text));
        return document;
    }

    private static string GetEditorParagraphText(RichTextBox box, int paragraphIndex)
    {
        return DocumentEditHelpers.GetParagraphText(box.EditorDocumentForTests.GetParagraph(paragraphIndex));
    }

    private static async Task RaiseTextInputAsync(RichTextBox box, string text)
    {
        box.Focus();
        await WaitForUiAsync();
        var textInput = new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = box,
            Text = text
        };
        box.RaiseEvent(textInput);
        await WaitForUiAsync();
    }

    private static async Task<(Window Window, StyleInclude Style)> ShowInWindowAsync(RichTextBox boxA, RichTextBox boxB)
    {
        var style = new StyleInclude(new Uri("avares://ProEdit.RichText.Avalonia/"))
        {
            Source = new Uri("avares://ProEdit.RichText.Avalonia/Themes/Generic.axaml")
        };
        Application.Current!.Styles.Add(style);

        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*")
        };
        boxA.SetValue(Grid.RowProperty, 0);
        boxB.SetValue(Grid.RowProperty, 1);
        panel.Children.Add(boxA);
        panel.Children.Add(boxB);

        var window = new Window
        {
            Content = panel,
            Width = 960,
            Height = 760
        };
        window.Show();
        await WaitForUiAsync();
        boxA.Focus();
        await WaitForUiAsync();
        return (window, style);
    }

    private static async Task WaitForUiAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { });
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
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

        Assert.True(condition(), "Timed out waiting for collaboration state.");
    }

    private sealed class CollaborationPairContext
    {
        public CollaborationPairContext(
            RichTextBox boxA,
            RichTextBox boxB,
            HubRealtimeSession sessionA,
            HubRealtimeSession sessionB,
            SessionBackedCollabUiService uiServiceA,
            SessionBackedCollabUiService uiServiceB,
            FixedIdentityService identityA,
            Window window,
            StyleInclude style)
        {
            BoxA = boxA;
            BoxB = boxB;
            SessionA = sessionA;
            SessionB = sessionB;
            UiServiceA = uiServiceA;
            UiServiceB = uiServiceB;
            IdentityA = identityA;
            Window = window;
            Style = style;
        }

        public RichTextBox BoxA { get; }
        public RichTextBox BoxB { get; }
        public HubRealtimeSession SessionA { get; }
        public HubRealtimeSession SessionB { get; }
        public SessionBackedCollabUiService UiServiceA { get; }
        public SessionBackedCollabUiService UiServiceB { get; }
        public FixedIdentityService IdentityA { get; }
        public Window Window { get; }
        public StyleInclude Style { get; }

        public async ValueTask DisposeAsync()
        {
            RichTextBoxCollaborationBehavior.SetIsEnabled(BoxA, false);
            RichTextBoxCollaborationBehavior.SetIsEnabled(BoxB, false);
            await SessionA.DisconnectAsync();
            await SessionB.DisconnectAsync();
            UiServiceA.Dispose();
            UiServiceB.Dispose();
            Window.Close();
            Application.Current!.Styles.Remove(Style);
        }
    }

    private sealed class SharedRealtimeHub
    {
        private readonly object _gate = new();
        private readonly List<HubRealtimeSession> _sessions = new();

        public void Register(HubRealtimeSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            lock (_gate)
            {
                _sessions.Add(session);
            }
        }

        public void Unregister(HubRealtimeSession session)
        {
            lock (_gate)
            {
                _sessions.Remove(session);
            }
        }

        public void BroadcastOps(HubRealtimeSession sender, CollabOpBatch batch)
        {
            var peers = CapturePeers(sender);
            for (var i = 0; i < peers.Length; i++)
            {
                peers[i].ReceiveOps(batch);
            }
        }

        public void BroadcastPresence(HubRealtimeSession sender, PresenceState presence, TimeSpan ttl)
        {
            var peers = CapturePeers(sender);
            for (var i = 0; i < peers.Length; i++)
            {
                peers[i].ReceivePresence(presence, ttl);
            }
        }

        public void BroadcastSnapshot(HubRealtimeSession sender, SnapshotMessage snapshot)
        {
            var peers = CapturePeers(sender);
            for (var i = 0; i < peers.Length; i++)
            {
                peers[i].ReceiveSnapshot(snapshot);
            }
        }

        private HubRealtimeSession[] CapturePeers(HubRealtimeSession sender)
        {
            lock (_gate)
            {
                var peers = new List<HubRealtimeSession>(_sessions.Count);
                for (var i = 0; i < _sessions.Count; i++)
                {
                    var candidate = _sessions[i];
                    if (!ReferenceEquals(candidate, sender))
                    {
                        peers.Add(candidate);
                    }
                }

                return peers.ToArray();
            }
        }
    }

    private sealed class HubRealtimeSession : ICollabRealtimeSession
    {
        private readonly SharedRealtimeHub _hub;
        private bool _connected;

        public HubRealtimeSession(SharedRealtimeHub hub, Guid documentId, Guid sessionId, Guid senderId)
        {
            _hub = hub;
            DocumentId = documentId;
            SessionId = sessionId;
            SenderId = senderId;
            _hub.Register(this);
        }

        public Guid DocumentId { get; }
        public Guid SessionId { get; }
        public Guid SenderId { get; }
        public int SubmittedBatchCount { get; private set; }

        public event EventHandler<CollabTransportStateChangedEventArgs>? TransportStateChanged;
        public event EventHandler<CollabOpsReceivedEventArgs>? OpsReceived;
        public event EventHandler<CollabSnapshotReceivedEventArgs>? SnapshotReceived;
        public event EventHandler<CollabPresenceEventArgs>? PresenceReceived;
        public event EventHandler<CollabErrorEventArgs>? ErrorReceived;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            _connected = true;
            TransportStateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Connected));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            _connected = false;
            TransportStateChanged?.Invoke(this, new CollabTransportStateChangedEventArgs(CollabTransportState.Disconnected));
            _hub.Unregister(this);
            return ValueTask.CompletedTask;
        }

        public ValueTask SubmitLocalAsync(CollabOpBatch batch, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(batch);
            SubmittedBatchCount++;
            if (_connected)
            {
                _hub.BroadcastOps(this, batch);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask SendPresenceAsync(
            PresenceState presence,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(presence);
            if (_connected)
            {
                _hub.BroadcastPresence(this, presence, timeToLive ?? TimeSpan.FromSeconds(10));
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask SendSnapshotAsync(SnapshotMessage snapshot, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (_connected)
            {
                _hub.BroadcastSnapshot(this, snapshot);
            }

            return ValueTask.CompletedTask;
        }

        public void ReceiveOps(CollabOpBatch batch)
        {
            if (!_connected)
            {
                return;
            }

            OpsReceived?.Invoke(this, new CollabOpsReceivedEventArgs(batch, DateTimeOffset.UtcNow));
        }

        public void ReceivePresence(PresenceState presence, TimeSpan ttl)
        {
            if (!_connected)
            {
                return;
            }

            PresenceReceived?.Invoke(this, new CollabPresenceEventArgs(presence, ttl, DateTimeOffset.UtcNow));
        }

        public void ReceiveSnapshot(SnapshotMessage snapshot)
        {
            if (!_connected)
            {
                return;
            }

            SnapshotReceived?.Invoke(this, new CollabSnapshotReceivedEventArgs(snapshot, DateTimeOffset.UtcNow));
        }

        public void ReceiveError(ErrorMessage error)
        {
            if (!_connected)
            {
                return;
            }

            ErrorReceived?.Invoke(this, new CollabErrorEventArgs(error, DateTimeOffset.UtcNow));
        }
    }

    private sealed class SessionBackedCollabUiService : ICollabUiService, IDisposable
    {
        private readonly ICollabRealtimeSession _session;
        private readonly ICollabIdentityService _identity;
        private readonly Dictionary<Guid, PresenceState> _presenceByUser = new();
        private readonly Dictionary<Guid, CollabParticipant> _participantsByUser = new();
        private IReadOnlyList<PresenceState> _presence = Array.Empty<PresenceState>();
        private IReadOnlyList<CollabParticipant> _participants = Array.Empty<CollabParticipant>();

        public SessionBackedCollabUiService(ICollabRealtimeSession session, ICollabIdentityService identity)
        {
            _session = session;
            _identity = identity;
            _session.TransportStateChanged += OnTransportStateChanged;
            _session.PresenceReceived += OnPresenceReceived;
            SetLocalParticipant();
        }

        public CollabConnectionState ConnectionState { get; private set; } = CollabConnectionState.Disconnected;
        public string? ConnectionMessage { get; private set; }
        public Guid DocumentId => _session.DocumentId;
        public Guid SessionId => _session.SessionId;
        public CollabTransportMode TransportMode { get; set; } = CollabTransportMode.LocalBroker;
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
            return _session.ConnectAsync(cancellationToken);
        }

        public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
        {
            return _session.DisconnectAsync(cancellationToken);
        }

        public ValueTask ShareAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
        {
            return _session.ConnectAsync(cancellationToken);
        }

        public void UpdatePresence(PresenceState presence, TimeSpan? timeToLive = null)
        {
            _presenceByUser[presence.UserId] = presence;
            UpdateParticipantFromPresence(presence);
            SnapshotPresence();
            RaiseStateChanged();
            _ = _session.SendPresenceAsync(presence, timeToLive);
        }

        public void UpdateParticipants(IReadOnlyList<CollabParticipant> participants)
        {
            _participantsByUser.Clear();
            for (var i = 0; i < participants.Count; i++)
            {
                var item = participants[i];
                _participantsByUser[item.UserId] = item;
            }

            SetLocalParticipant();
            SnapshotParticipants();
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

        public bool TryGetPresence(Guid userId, out PresenceState presence)
        {
            return _presenceByUser.TryGetValue(userId, out presence!);
        }

        public void Dispose()
        {
            _session.TransportStateChanged -= OnTransportStateChanged;
            _session.PresenceReceived -= OnPresenceReceived;
        }

        private void OnTransportStateChanged(object? sender, CollabTransportStateChangedEventArgs e)
        {
            var connectionState = e.State switch
            {
                CollabTransportState.Connecting => CollabConnectionState.Connecting,
                CollabTransportState.Connected => CollabConnectionState.Connected,
                CollabTransportState.Error => CollabConnectionState.Error,
                _ => CollabConnectionState.Disconnected
            };

            SetConnectionState(connectionState, e.Message);
        }

        private void OnPresenceReceived(object? sender, CollabPresenceEventArgs e)
        {
            var presence = e.Presence;
            _presenceByUser[presence.UserId] = presence;
            UpdateParticipantFromPresence(presence);
            SnapshotPresence();
            RaiseStateChanged();
        }

        private void SetLocalParticipant()
        {
            _participantsByUser[_identity.UserId] = new CollabParticipant(
                _identity.UserId,
                _identity.DisplayName,
                _identity.Color,
                DateTimeOffset.UtcNow,
                IsLocal: true);
            SnapshotParticipants();
        }

        private void UpdateParticipantFromPresence(PresenceState presence)
        {
            var isLocal = presence.UserId == _identity.UserId;
            var color = !string.IsNullOrWhiteSpace(presence.Color)
                ? presence.Color
                : isLocal ? _identity.Color : "#4C78A8";
            var displayName = isLocal ? _identity.DisplayName : presence.DisplayName;
            _participantsByUser[presence.UserId] = new CollabParticipant(
                presence.UserId,
                displayName,
                color,
                presence.UpdatedAtUtc,
                isLocal);
            SnapshotParticipants();
        }

        private void SnapshotParticipants()
        {
            var participants = new CollabParticipant[_participantsByUser.Count];
            var index = 0;
            foreach (var participant in _participantsByUser.Values)
            {
                participants[index++] = participant;
            }

            _participants = participants;
        }

        private void SnapshotPresence()
        {
            var presence = new PresenceState[_presenceByUser.Count];
            var index = 0;
            foreach (var state in _presenceByUser.Values)
            {
                presence[index++] = state;
            }

            _presence = presence;
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FixedIdentityService : ICollabIdentityService
    {
        public FixedIdentityService(Guid userId, string displayName, string color)
        {
            UserId = userId;
            DisplayName = displayName;
            Color = color;
        }

        public Guid UserId { get; }
        public string DisplayName { get; }
        public string Color { get; }
    }
}

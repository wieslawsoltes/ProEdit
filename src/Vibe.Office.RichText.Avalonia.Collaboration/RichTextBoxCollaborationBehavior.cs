using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.Collaboration.UI;
using Vibe.Office.RichText.Avalonia;

namespace Vibe.Office.RichText.Avalonia.Collaboration;

/// <summary>
/// Provides attached collaboration wiring for <see cref="RichTextBox"/> without altering its public WPF-like API.
/// </summary>
public static class RichTextBoxCollaborationBehavior
{
    private sealed class BehaviorOwner
    {
    }

    private static readonly ConditionalWeakTable<RichTextBox, BehaviorState> States = new();

    /// <summary>
    /// Identifies the <c>IsEnabled</c> attached property.
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, bool>("IsEnabled");

    /// <summary>
    /// Identifies the <c>Session</c> attached property.
    /// </summary>
    public static readonly AttachedProperty<ICollabRealtimeSession?> SessionProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, ICollabRealtimeSession?>("Session");

    /// <summary>
    /// Identifies the <c>CollabUiService</c> attached property.
    /// </summary>
    public static readonly AttachedProperty<ICollabUiService?> CollabUiServiceProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, ICollabUiService?>("CollabUiService");

    /// <summary>
    /// Identifies the <c>CollabIdentityService</c> attached property.
    /// </summary>
    public static readonly AttachedProperty<ICollabIdentityService?> CollabIdentityServiceProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, ICollabIdentityService?>("CollabIdentityService");

    /// <summary>
    /// Identifies the <c>AuthorResolver</c> attached property.
    /// </summary>
    public static readonly AttachedProperty<Func<Guid, string?>?> AuthorResolverProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, Func<Guid, string?>?>("AuthorResolver");

    /// <summary>
    /// Identifies the <c>ConnectionState</c> attached diagnostics property.
    /// </summary>
    public static readonly AttachedProperty<CollabConnectionState> ConnectionStateProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, CollabConnectionState>(
            "ConnectionState",
            CollabConnectionState.Disconnected);

    /// <summary>
    /// Identifies the <c>ConnectionMessage</c> attached diagnostics property.
    /// </summary>
    public static readonly AttachedProperty<string?> ConnectionMessageProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, string?>("ConnectionMessage");

    /// <summary>
    /// Identifies the <c>SyncLag</c> attached diagnostics property.
    /// </summary>
    public static readonly AttachedProperty<TimeSpan> SyncLagProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, TimeSpan>("SyncLag");

    /// <summary>
    /// Identifies the <c>OpQueueDepth</c> attached diagnostics property.
    /// </summary>
    public static readonly AttachedProperty<int> OpQueueDepthProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, int>("OpQueueDepth");

    /// <summary>
    /// Identifies the <c>SnapshotAge</c> attached diagnostics property.
    /// </summary>
    public static readonly AttachedProperty<TimeSpan> SnapshotAgeProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, TimeSpan>("SnapshotAge");

    /// <summary>
    /// Identifies the <c>ParticipantCount</c> attached diagnostics property.
    /// </summary>
    public static readonly AttachedProperty<int> ParticipantCountProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, int>("ParticipantCount");

    /// <summary>
    /// Identifies the <c>PresenceCount</c> attached diagnostics property.
    /// </summary>
    public static readonly AttachedProperty<int> PresenceCountProperty =
        AvaloniaProperty.RegisterAttached<BehaviorOwner, RichTextBox, int>("PresenceCount");

    static RichTextBoxCollaborationBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<RichTextBox>(OnBehaviorPropertyChanged);
        SessionProperty.Changed.AddClassHandler<RichTextBox>(OnBehaviorPropertyChanged);
        CollabUiServiceProperty.Changed.AddClassHandler<RichTextBox>(OnBehaviorPropertyChanged);
        CollabIdentityServiceProperty.Changed.AddClassHandler<RichTextBox>(OnBehaviorPropertyChanged);
        AuthorResolverProperty.Changed.AddClassHandler<RichTextBox>(OnBehaviorPropertyChanged);
    }

    /// <summary>
    /// Gets whether collaboration behavior is enabled.
    /// </summary>
    public static bool GetIsEnabled(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(IsEnabledProperty);
    }

    /// <summary>
    /// Sets whether collaboration behavior is enabled.
    /// </summary>
    public static void SetIsEnabled(RichTextBox richTextBox, bool value)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        richTextBox.SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets the collaboration session.
    /// </summary>
    public static ICollabRealtimeSession? GetSession(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(SessionProperty);
    }

    /// <summary>
    /// Sets the collaboration session.
    /// </summary>
    public static void SetSession(RichTextBox richTextBox, ICollabRealtimeSession? value)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        richTextBox.SetValue(SessionProperty, value);
    }

    /// <summary>
    /// Gets the collaboration UI service.
    /// </summary>
    public static ICollabUiService? GetCollabUiService(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(CollabUiServiceProperty);
    }

    /// <summary>
    /// Sets the collaboration UI service.
    /// </summary>
    public static void SetCollabUiService(RichTextBox richTextBox, ICollabUiService? value)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        richTextBox.SetValue(CollabUiServiceProperty, value);
    }

    /// <summary>
    /// Gets the collaboration identity service.
    /// </summary>
    public static ICollabIdentityService? GetCollabIdentityService(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(CollabIdentityServiceProperty);
    }

    /// <summary>
    /// Sets the collaboration identity service.
    /// </summary>
    public static void SetCollabIdentityService(RichTextBox richTextBox, ICollabIdentityService? value)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        richTextBox.SetValue(CollabIdentityServiceProperty, value);
    }

    /// <summary>
    /// Gets the optional author resolver used for remote operation attribution.
    /// </summary>
    public static Func<Guid, string?>? GetAuthorResolver(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(AuthorResolverProperty);
    }

    /// <summary>
    /// Sets the optional author resolver used for remote operation attribution.
    /// </summary>
    public static void SetAuthorResolver(RichTextBox richTextBox, Func<Guid, string?>? value)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        richTextBox.SetValue(AuthorResolverProperty, value);
    }

    /// <summary>
    /// Gets the collaboration connection state diagnostics value.
    /// </summary>
    public static CollabConnectionState GetConnectionState(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(ConnectionStateProperty);
    }

    /// <summary>
    /// Gets the collaboration connection message diagnostics value.
    /// </summary>
    public static string? GetConnectionMessage(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(ConnectionMessageProperty);
    }

    /// <summary>
    /// Gets the synchronization lag diagnostics value.
    /// </summary>
    public static TimeSpan GetSyncLag(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(SyncLagProperty);
    }

    /// <summary>
    /// Gets the operation queue depth diagnostics value.
    /// </summary>
    public static int GetOpQueueDepth(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(OpQueueDepthProperty);
    }

    /// <summary>
    /// Gets the snapshot age diagnostics value.
    /// </summary>
    public static TimeSpan GetSnapshotAge(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(SnapshotAgeProperty);
    }

    /// <summary>
    /// Gets the participant count diagnostics value.
    /// </summary>
    public static int GetParticipantCount(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(ParticipantCountProperty);
    }

    /// <summary>
    /// Gets the presence count diagnostics value.
    /// </summary>
    public static int GetPresenceCount(RichTextBox richTextBox)
    {
        ArgumentNullException.ThrowIfNull(richTextBox);
        return richTextBox.GetValue(PresenceCountProperty);
    }

    private static void OnBehaviorPropertyChanged(RichTextBox richTextBox, AvaloniaPropertyChangedEventArgs args)
    {
        var state = States.GetValue(richTextBox, static box => new BehaviorState(box));
        state.Apply();
    }

    private sealed class BehaviorState
    {
        private readonly RichTextBox _owner;
        private readonly IRichTextBoxCollaborationAdapter _adapter;
        private ICollabRealtimeSession? _attachedSession;
        private Func<Guid, string?>? _attachedResolver;
        private ICollabUiService? _registeredUiService;
        private ICollabIdentityService? _registeredIdentityService;
        private bool _hasRegisteredUiService;
        private bool _hasRegisteredIdentityService;
        private bool _isUiStateSubscribed;
        private bool _updating;

        public BehaviorState(RichTextBox owner)
        {
            _owner = owner;
            _adapter = (IRichTextBoxCollaborationAdapter)owner;
            _adapter.EditorSessionRebuilt += OnEditorSessionRebuilt;
            _owner.DetachedFromVisualTree += OnDetachedFromVisualTree;
            _owner.AttachedToVisualTree += OnAttachedToVisualTree;
        }

        public void Apply()
        {
            Apply(forceSessionRefresh: false);
        }

        private void Apply(bool forceSessionRefresh)
        {
            if (_updating)
            {
                return;
            }

            _updating = true;
            try
            {
                var isEnabled = _owner.GetValue(IsEnabledProperty);
                var session = _owner.GetValue(SessionProperty);
                var resolver = _owner.GetValue(AuthorResolverProperty);
                var uiService = _owner.GetValue(CollabUiServiceProperty);
                var identityService = _owner.GetValue(CollabIdentityServiceProperty);

                if (!isEnabled)
                {
                    DetachIfNeeded();
                    UnregisterUiStateEvents();
                    UnregisterRegisteredServices();
                    ResetDiagnostics();
                    return;
                }

                var servicesChanged = RegisterServices(uiService, identityService);
                AttachIfNeeded(session, resolver, servicesChanged || forceSessionRefresh);
                RefreshDiagnostics();
            }
            finally
            {
                _updating = false;
            }
        }

        private bool RegisterServices(ICollabUiService? uiService, ICollabIdentityService? identityService)
        {
            var changed = false;
            if (uiService is null)
            {
                if (_hasRegisteredUiService)
                {
                    UnregisterUiStateEvents();
                    _adapter.UnregisterService<ICollabUiService>();
                    _hasRegisteredUiService = false;
                }

                if (_registeredUiService is not null)
                {
                    _registeredUiService = null;
                    changed = true;
                }
            }
            else
            {
                var requiresUiRegistration =
                    !ReferenceEquals(uiService, _registeredUiService)
                    || !_hasRegisteredUiService
                    || !_adapter.TryGetService<ICollabUiService>(out var resolvedUiService)
                    || !ReferenceEquals(resolvedUiService, uiService);

                if (requiresUiRegistration)
                {
                    UnregisterUiStateEvents();

                    if (_hasRegisteredUiService)
                    {
                        _adapter.UnregisterService<ICollabUiService>();
                        _hasRegisteredUiService = false;
                    }

                    _registeredUiService = uiService;
                    _adapter.RegisterService(uiService);
                    _hasRegisteredUiService = true;
                    uiService.StateChanged += OnUiServiceStateChanged;
                    _isUiStateSubscribed = true;
                    changed = true;
                }
                else if (!_isUiStateSubscribed)
                {
                    uiService.StateChanged += OnUiServiceStateChanged;
                    _isUiStateSubscribed = true;
                }
            }

            if (identityService is null)
            {
                if (_hasRegisteredIdentityService)
                {
                    _adapter.UnregisterService<ICollabIdentityService>();
                    _hasRegisteredIdentityService = false;
                }

                if (_registeredIdentityService is not null)
                {
                    _registeredIdentityService = null;
                    changed = true;
                }
            }
            else
            {
                var requiresIdentityRegistration =
                    !ReferenceEquals(identityService, _registeredIdentityService)
                    || !_hasRegisteredIdentityService
                    || !_adapter.TryGetService<ICollabIdentityService>(out var resolvedIdentityService)
                    || !ReferenceEquals(resolvedIdentityService, identityService);

                if (requiresIdentityRegistration)
                {
                    if (_hasRegisteredIdentityService)
                    {
                        _adapter.UnregisterService<ICollabIdentityService>();
                        _hasRegisteredIdentityService = false;
                    }

                    _registeredIdentityService = identityService;
                    _adapter.RegisterService(identityService);
                    _hasRegisteredIdentityService = true;
                    changed = true;
                }
            }

            return changed;
        }

        private void AttachIfNeeded(ICollabRealtimeSession? session, Func<Guid, string?>? resolver, bool forceReattach)
        {
            if (session is null)
            {
                DetachIfNeeded();
                return;
            }

            if (!ReferenceEquals(session, _attachedSession) || !ReferenceEquals(resolver, _attachedResolver) || forceReattach)
            {
                _adapter.AttachSession(session, resolver);
                _attachedSession = session;
                _attachedResolver = resolver;
            }
        }

        private void DetachIfNeeded()
        {
            if (_attachedSession is null)
            {
                return;
            }

            _adapter.DetachSession();
            _attachedSession = null;
            _attachedResolver = null;
        }

        private void RefreshDiagnostics()
        {
            var uiService = _registeredUiService;
            if (uiService is null)
            {
                ResetDiagnostics();
                return;
            }

            _owner.SetValue(ConnectionStateProperty, uiService.ConnectionState);
            _owner.SetValue(ConnectionMessageProperty, uiService.ConnectionMessage);
            _owner.SetValue(SyncLagProperty, uiService.SyncLag);
            _owner.SetValue(OpQueueDepthProperty, uiService.OpQueueDepth);
            _owner.SetValue(SnapshotAgeProperty, uiService.SnapshotAge);
            _owner.SetValue(ParticipantCountProperty, uiService.Participants.Count);
            _owner.SetValue(PresenceCountProperty, uiService.Presence.Count);
        }

        private void ResetDiagnostics()
        {
            _owner.SetValue(ConnectionStateProperty, CollabConnectionState.Disconnected);
            _owner.SetValue(ConnectionMessageProperty, null);
            _owner.SetValue(SyncLagProperty, TimeSpan.Zero);
            _owner.SetValue(OpQueueDepthProperty, 0);
            _owner.SetValue(SnapshotAgeProperty, TimeSpan.Zero);
            _owner.SetValue(ParticipantCountProperty, 0);
            _owner.SetValue(PresenceCountProperty, 0);
        }

        private void UnregisterUiStateEvents()
        {
            if (_registeredUiService is not null && _isUiStateSubscribed)
            {
                _registeredUiService.StateChanged -= OnUiServiceStateChanged;
            }

            _isUiStateSubscribed = false;
        }

        private void UnregisterRegisteredServices()
        {
            if (_hasRegisteredUiService)
            {
                _adapter.UnregisterService<ICollabUiService>();
                _hasRegisteredUiService = false;
            }

            if (_hasRegisteredIdentityService)
            {
                _adapter.UnregisterService<ICollabIdentityService>();
                _hasRegisteredIdentityService = false;
            }

            _registeredUiService = null;
            _registeredIdentityService = null;
        }

        private void OnUiServiceStateChanged(object? sender, EventArgs e)
        {
            // Diagnostics are exposed through attached properties for VM-free host bindings.
            Dispatcher.UIThread.Post(RefreshDiagnostics);
        }

        private void OnEditorSessionRebuilt(object? sender, EventArgs e)
        {
            Apply(forceSessionRefresh: true);
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            DetachIfNeeded();
            UnregisterUiStateEvents();
            UnregisterRegisteredServices();
            ResetDiagnostics();
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            Apply(forceSessionRefresh: true);
        }
    }
}

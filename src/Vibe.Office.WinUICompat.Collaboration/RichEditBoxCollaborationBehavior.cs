using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Vibe.Office.Collaboration.Protocol;
using Vibe.Office.Collaboration.UI;
using Vibe.Office.WinUICompat.Controls;

namespace Vibe.Office.WinUICompat.Collaboration;

public static class RichEditBoxCollaborationBehavior
{
    private static readonly ConditionalWeakTable<RichEditBox, BehaviorState> States = new();

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(false, OnBehaviorPropertyChanged));

    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.RegisterAttached(
            "Session",
            typeof(ICollabRealtimeSession),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(null, OnBehaviorPropertyChanged));

    public static readonly DependencyProperty CollabUiServiceProperty =
        DependencyProperty.RegisterAttached(
            "CollabUiService",
            typeof(ICollabUiService),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(null, OnBehaviorPropertyChanged));

    public static readonly DependencyProperty CollabIdentityServiceProperty =
        DependencyProperty.RegisterAttached(
            "CollabIdentityService",
            typeof(ICollabIdentityService),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(null, OnBehaviorPropertyChanged));

    public static readonly DependencyProperty AuthorResolverProperty =
        DependencyProperty.RegisterAttached(
            "AuthorResolver",
            typeof(Func<Guid, string?>),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(null, OnBehaviorPropertyChanged));

    public static readonly DependencyProperty ConnectionStateProperty =
        DependencyProperty.RegisterAttached(
            "ConnectionState",
            typeof(CollabConnectionState),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(CollabConnectionState.Disconnected));

    public static readonly DependencyProperty ConnectionMessageProperty =
        DependencyProperty.RegisterAttached(
            "ConnectionMessage",
            typeof(string),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SyncLagProperty =
        DependencyProperty.RegisterAttached(
            "SyncLag",
            typeof(TimeSpan),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(TimeSpan.Zero));

    public static readonly DependencyProperty OpQueueDepthProperty =
        DependencyProperty.RegisterAttached(
            "OpQueueDepth",
            typeof(int),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(0));

    public static readonly DependencyProperty SnapshotAgeProperty =
        DependencyProperty.RegisterAttached(
            "SnapshotAge",
            typeof(TimeSpan),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(TimeSpan.Zero));

    public static readonly DependencyProperty ParticipantCountProperty =
        DependencyProperty.RegisterAttached(
            "ParticipantCount",
            typeof(int),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(0));

    public static readonly DependencyProperty PresenceCountProperty =
        DependencyProperty.RegisterAttached(
            "PresenceCount",
            typeof(int),
            typeof(RichEditBoxCollaborationBehavior),
            new PropertyMetadata(0));

    public static bool GetIsEnabled(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (bool)richEditBox.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(RichEditBox richEditBox, bool value)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        richEditBox.SetValue(IsEnabledProperty, value);
    }

    public static ICollabRealtimeSession? GetSession(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (ICollabRealtimeSession?)richEditBox.GetValue(SessionProperty);
    }

    public static void SetSession(RichEditBox richEditBox, ICollabRealtimeSession? value)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        richEditBox.SetValue(SessionProperty, value);
    }

    public static ICollabUiService? GetCollabUiService(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (ICollabUiService?)richEditBox.GetValue(CollabUiServiceProperty);
    }

    public static void SetCollabUiService(RichEditBox richEditBox, ICollabUiService? value)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        richEditBox.SetValue(CollabUiServiceProperty, value);
    }

    public static ICollabIdentityService? GetCollabIdentityService(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (ICollabIdentityService?)richEditBox.GetValue(CollabIdentityServiceProperty);
    }

    public static void SetCollabIdentityService(RichEditBox richEditBox, ICollabIdentityService? value)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        richEditBox.SetValue(CollabIdentityServiceProperty, value);
    }

    public static Func<Guid, string?>? GetAuthorResolver(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (Func<Guid, string?>?)richEditBox.GetValue(AuthorResolverProperty);
    }

    public static void SetAuthorResolver(RichEditBox richEditBox, Func<Guid, string?>? value)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        richEditBox.SetValue(AuthorResolverProperty, value);
    }

    public static CollabConnectionState GetConnectionState(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (CollabConnectionState)richEditBox.GetValue(ConnectionStateProperty);
    }

    public static string? GetConnectionMessage(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (string?)richEditBox.GetValue(ConnectionMessageProperty);
    }

    public static TimeSpan GetSyncLag(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (TimeSpan)richEditBox.GetValue(SyncLagProperty);
    }

    public static int GetOpQueueDepth(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (int)richEditBox.GetValue(OpQueueDepthProperty);
    }

    public static TimeSpan GetSnapshotAge(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (TimeSpan)richEditBox.GetValue(SnapshotAgeProperty);
    }

    public static int GetParticipantCount(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (int)richEditBox.GetValue(ParticipantCountProperty);
    }

    public static int GetPresenceCount(RichEditBox richEditBox)
    {
        ArgumentNullException.ThrowIfNull(richEditBox);
        return (int)richEditBox.GetValue(PresenceCountProperty);
    }

    private static void OnBehaviorPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not RichEditBox richEditBox)
        {
            return;
        }

        GetState(richEditBox).Apply();
    }

    private static BehaviorState GetState(RichEditBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return States.GetValue(box, static key => new BehaviorState(key));
    }

    private sealed class BehaviorState
    {
        private readonly RichEditBox _owner;
        private readonly IRichEditBoxCollaborationAdapter _adapter;
        private ICollabRealtimeSession? _attachedSession;
        private Func<Guid, string?>? _attachedResolver;
        private ICollabUiService? _registeredUiService;
        private ICollabIdentityService? _registeredIdentityService;
        private bool _hasRegisteredUiService;
        private bool _hasRegisteredIdentityService;
        private bool _isUiStateSubscribed;
        private bool _updating;

        public BehaviorState(RichEditBox owner)
        {
            _owner = owner;
            _adapter = (IRichEditBoxCollaborationAdapter)owner;
            _adapter.SessionRebuilt += OnSessionRebuilt;
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
                var isEnabled = (bool)_owner.GetValue(IsEnabledProperty);
                var session = (ICollabRealtimeSession?)_owner.GetValue(SessionProperty);
                var resolver = (Func<Guid, string?>?)_owner.GetValue(AuthorResolverProperty);
                var uiService = (ICollabUiService?)_owner.GetValue(CollabUiServiceProperty);
                var identityService = (ICollabIdentityService?)_owner.GetValue(CollabIdentityServiceProperty);

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
                var requiresRegistration =
                    !_hasRegisteredUiService
                    || !ReferenceEquals(uiService, _registeredUiService)
                    || !_adapter.TryGetService<ICollabUiService>(out var existingUiService)
                    || !ReferenceEquals(existingUiService, uiService);

                if (requiresRegistration)
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
                var requiresRegistration =
                    !_hasRegisteredIdentityService
                    || !ReferenceEquals(identityService, _registeredIdentityService)
                    || !_adapter.TryGetService<ICollabIdentityService>(out var existingIdentityService)
                    || !ReferenceEquals(existingIdentityService, identityService);

                if (requiresRegistration)
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

            if (!ReferenceEquals(_attachedSession, session) || !ReferenceEquals(_attachedResolver, resolver) || forceReattach)
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
            RefreshDiagnostics();
        }

        private void OnSessionRebuilt(object? sender, EventArgs e)
        {
            Apply(forceSessionRefresh: true);
        }
    }
}

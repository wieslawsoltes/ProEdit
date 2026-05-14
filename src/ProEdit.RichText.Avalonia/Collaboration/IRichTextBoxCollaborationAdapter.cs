using ProEdit.Collaboration.Protocol;

namespace ProEdit.RichText.Avalonia;

/// <summary>
/// Internal collaboration adapter contract used by RichTextBox extension integrations.
/// </summary>
internal interface IRichTextBoxCollaborationAdapter
{
    /// <summary>
    /// Raised after the internal editor session is rebuilt.
    /// </summary>
    event EventHandler? EditorSessionRebuilt;

    /// <summary>
    /// Attaches an active collaboration session.
    /// </summary>
    void AttachSession(ICollabRealtimeSession session, Func<Guid, string?>? authorResolver = null);

    /// <summary>
    /// Detaches the current collaboration session.
    /// </summary>
    void DetachSession();

    /// <summary>
    /// Registers an editor service for collaboration integration.
    /// </summary>
    void RegisterService<T>(T service) where T : class;

    /// <summary>
    /// Unregisters an editor service for collaboration integration.
    /// </summary>
    bool UnregisterService<T>() where T : class;

    /// <summary>
    /// Attempts to resolve a collaboration-related editor service.
    /// </summary>
    bool TryGetService<T>(out T service) where T : class;
}

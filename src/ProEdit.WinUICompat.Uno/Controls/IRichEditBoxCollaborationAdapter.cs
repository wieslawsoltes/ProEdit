using ProEdit.Collaboration.Protocol;

namespace ProEdit.WinUICompat.Controls;

public interface IRichEditBoxCollaborationAdapter
{
    event EventHandler? SessionRebuilt;

    void AttachSession(ICollabRealtimeSession session, Func<Guid, string?>? authorResolver = null);

    void DetachSession();

    void RegisterService<T>(T service) where T : class;

    bool UnregisterService<T>() where T : class;

    bool TryGetService<T>(out T service) where T : class;
}

using Vibe.Office.Editing;

namespace Vibe.Office.Collaboration.Editor;

public sealed class CollabGestureRecorder : ICollabGestureRecorder
{
    public event EventHandler<CollabGestureEventArgs>? GestureStarted;
    public event EventHandler<CollabGestureEventArgs>? GestureEnded;

    public CollabGestureToken BeginGesture(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Gesture name is required.", nameof(name));
        }

        var token = new CollabGestureToken(Guid.NewGuid(), name);
        GestureStarted?.Invoke(this, new CollabGestureEventArgs(token));
        return token;
    }

    public void EndGesture(CollabGestureToken token)
    {
        GestureEnded?.Invoke(this, new CollabGestureEventArgs(token));
    }
}

public sealed class CollabGestureEventArgs : EventArgs
{
    public CollabGestureEventArgs(CollabGestureToken token)
    {
        Token = token;
    }

    public CollabGestureToken Token { get; }
}

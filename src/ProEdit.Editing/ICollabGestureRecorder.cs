namespace ProEdit.Editing;

public readonly record struct CollabGestureToken(Guid Id, string Name);

public interface ICollabGestureRecorder
{
    CollabGestureToken BeginGesture(string name);
    void EndGesture(CollabGestureToken token);
}

namespace Vibe.Office.Editing;

public sealed class ActivateContentControlCommand : IEditorUndoableCommand
{
    public ContentControlHit Hit { get; }
    public ContentControlActivationSource Source { get; }
    public EditorModifiers Modifiers { get; }
    public IContentControlInteractionService? InteractionService { get; }
    public bool IsUndoable => true;

    public ActivateContentControlCommand(
        ContentControlHit hit,
        ContentControlActivationSource source,
        EditorModifiers modifiers,
        IContentControlInteractionService? interactionService)
    {
        Hit = hit;
        Source = source;
        Modifiers = modifiers;
        InteractionService = interactionService;
    }

    public void Execute(IEditorMutableSession session)
    {
        if (session is IContentControlInteractionSession handler)
        {
            handler.TryActivateContentControl(Hit, Source, Modifiers, InteractionService);
        }
    }
}

namespace Vibe.Office.Editing;

public interface IEditorCommandObserver
{
    void OnCommandExecuted(string commandId, object? payload, bool recordHistory);
}

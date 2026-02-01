using Vibe.Office.Documents.Formats;

namespace Vibe.Office.Editing;

public interface IEditorFormatProfileService
{
    DocumentFormatProfile? CurrentProfile { get; set; }
    Func<string, bool>? CommandPolicy { get; set; }
    bool CanExecuteCommand(string commandId);
}

public sealed class EditorFormatProfileService : IEditorFormatProfileService
{
    public DocumentFormatProfile? CurrentProfile { get; set; }
    public Func<string, bool>? CommandPolicy { get; set; }

    public bool CanExecuteCommand(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            return false;
        }

        return CommandPolicy?.Invoke(commandId) ?? true;
    }
}

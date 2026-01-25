using Vibe.Office.Editing;

namespace Vibe.Office.Macros;

public sealed class MacroCommandFilter : IMacroCommandFilter
{
    private static readonly HashSet<string> BlockedCommandIds = new(StringComparer.OrdinalIgnoreCase)
    {
        EditorViewCommandIds.Macros.Open,
        EditorViewCommandIds.Macros.RecordMacro,
        EditorHomeCommandIds.Editing.Undo,
        EditorHomeCommandIds.Editing.Redo
    };

    public bool IsRecordable(string commandId, object? payload, bool recordHistory)
    {
        if (!recordHistory || string.IsNullOrWhiteSpace(commandId))
        {
            return false;
        }

        return !BlockedCommandIds.Contains(commandId);
    }
}

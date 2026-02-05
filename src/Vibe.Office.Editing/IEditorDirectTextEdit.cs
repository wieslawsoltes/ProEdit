using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

/// <summary>
/// Applies text edits without mutating the current selection or caret.
/// </summary>
public interface IEditorDirectTextEdit
{
    void InsertTextAt(TextPosition position, ReadOnlySpan<char> text);
    void DeleteRange(TextRange range);
}

using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Office.Editing;

public interface IEditorSession
{
    Document Document { get; }
    LayoutSettings LayoutSettings { get; }
    DocumentLayout Layout { get; }
    TextPosition Caret { get; }
    TextRange Selection { get; }
    Guid? SelectedFloatingObjectId { get; }
    IReadOnlyList<int> DirtyPages { get; }
    long DirtyVersion { get; }

    event EventHandler? Changed;
}

public interface IEditorMutableSession : IEditorSession
{
    void UpdateLayout(float viewportWidth, float viewportHeight);
    void RefreshLayout();

    void InsertText(ReadOnlySpan<char> text);
    void InsertEquation(MathElement root, TextStyleProperties? style = null, string? styleId = null);
    void InsertParagraphBreak();

    void Backspace();
    void DeleteForward();

    void MoveLeft(bool extendSelection);
    void MoveRight(bool extendSelection);
    void MoveUp(bool extendSelection);
    void MoveDown(bool extendSelection);

    void SetCaretFromPoint(float x, float y, bool extendSelection);
    void SetSelection(TextRange selection);
    bool TrySelectFirstFloatingObject();
}

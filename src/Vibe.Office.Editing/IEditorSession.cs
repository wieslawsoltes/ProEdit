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
    IReadOnlyList<TextRange> SelectionRanges { get; }
    IReadOnlyList<TableSelectionRange> TableSelections { get; }
    Guid? SelectedFloatingObjectId { get; }
    IReadOnlyList<Guid> SelectedFloatingObjectIds { get; }
    IReadOnlyList<int> DirtyPages { get; }
    long DirtyVersion { get; }

    event EventHandler? Changed;

    bool TryGetCaretPoint(out DocPoint point, out int lineIndex);
}

public interface IEditorMutableSession : IEditorSession
{
    void UpdateLayout(float viewportWidth, float viewportHeight);
    void RefreshLayout();

    void InsertText(ReadOnlySpan<char> text);
    void InsertEquation(MathElement root, TextStyleProperties? style = null, string? styleId = null);
    void InsertParagraphBreak();
    void InsertInline(Inline inline);
    void InsertInlines(IReadOnlyList<Inline> inlines);
    void InsertBlock(Block block);

    void Backspace();
    void DeleteForward();

    void MoveLeft(bool extendSelection);
    void MoveRight(bool extendSelection);
    void MoveUp(bool extendSelection);
    void MoveDown(bool extendSelection);
    void MoveLineStart(bool extendSelection);
    void MoveLineEnd(bool extendSelection);
    void MoveDocumentStart(bool extendSelection);
    void MoveDocumentEnd(bool extendSelection);
    void MovePageUp(bool extendSelection);
    void MovePageDown(bool extendSelection);
    void SelectAll();

    void SetCaretFromPoint(float x, float y, bool extendSelection);
    void SetCaretFromPoint(float x, float y, SelectionUpdateMode mode);
    void SetSelection(TextRange selection);
    void SetSelection(TextRange selection, SelectionUpdateMode mode);
    bool TrySelectWordFromPoint(float x, float y, SelectionUpdateMode mode);
    bool TrySelectParagraphFromPoint(float x, float y, SelectionUpdateMode mode);
    bool TrySelectFirstFloatingObject();
}

using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorSelectionStateAdapter : ISelectionState
{
    private readonly IEditorSession _session;

    public EditorSelectionStateAdapter(IEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public EditorSelectionSnapshot GetSnapshot()
    {
        var caret = _session.Caret;
        var selection = _session.Selection;
        var isCollapsed = selection.IsEmpty;
        var selectedFloating = _session.SelectedFloatingObjectId;
        var kind = selectedFloating.HasValue
            ? EditorSelectionKind.FloatingObject
            : isCollapsed ? EditorSelectionKind.Caret : EditorSelectionKind.Range;

        var isInTable = false;
        var isInList = false;
        if (_session.Document.ParagraphCount > 0)
        {
            var paragraphIndex = Math.Clamp(caret.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
            var location = _session.Document.GetParagraphLocation(paragraphIndex);
            isInTable = location.IsInTable;
            isInList = location.Paragraph.ListInfo?.Kind != ListKind.None;
        }

        return new EditorSelectionSnapshot(
            kind,
            isCollapsed,
            false,
            false,
            isInTable,
            isInList,
            caret,
            selection,
            selectedFloating);
    }
}

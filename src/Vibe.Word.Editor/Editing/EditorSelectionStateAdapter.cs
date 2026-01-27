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
        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount > 0)
        {
            var paragraphIndex = Math.Clamp(caret.ParagraphIndex, 0, paragraphCount - 1);
            var paragraph = _session.GetParagraphFast(paragraphIndex);
            isInList = paragraph.ListInfo?.Kind != ListKind.None;

            if (_session.TryGetParagraphLineRangeFast(paragraphIndex, out var range) && range.Count > 0)
            {
                var layout = _session.Layout;
                var lineIndex = Math.Clamp(range.Start, 0, layout.Lines.Count - 1);
                isInTable = layout.Lines.Count > 0 && layout.Lines[lineIndex].IsInTable;
            }
            else
            {
                var location = _session.Document.GetParagraphLocation(paragraphIndex);
                isInTable = location.IsInTable;
            }
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

using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Office.Printing.Documents;

public static class DocumentPrintSelection
{
    public static bool TryResolveSelectionPageRange(DocumentLayout layout, TextRange selection, out (int start, int end) range)
    {
        range = default;
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        var startLine = EditorSelectionService.FindLineIndexForPosition(layout, selection.Start, out _);
        var endLine = EditorSelectionService.FindLineIndexForPosition(layout, selection.End, out _);
        var startPage = layout.LineIndex.GetPageForLine(startLine);
        var endPage = layout.LineIndex.GetPageForLine(endLine);
        if (startPage < 0 || endPage < 0)
        {
            return false;
        }

        range = (startPage, endPage);
        return true;
    }
}

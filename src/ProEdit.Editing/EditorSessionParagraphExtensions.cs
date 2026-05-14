using ProEdit.Documents;
using ProEdit.Layout;

namespace ProEdit.Editing;

public static class EditorSessionParagraphExtensions
{
    public static int GetParagraphCountFast(this IEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var count = session.Layout.Paragraphs.Count;
        return count == 0 ? session.Document.ParagraphCount : count;
    }

    public static ParagraphBlock GetParagraphFast(this IEditorSession session, int paragraphIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        var paragraphs = session.Layout.Paragraphs;
        if ((uint)paragraphIndex < (uint)paragraphs.Count)
        {
            return paragraphs[paragraphIndex];
        }

        return session.Document.GetParagraph(paragraphIndex);
    }

    public static bool TryGetParagraphLineRangeFast(this IEditorSession session, int paragraphIndex, out LineRange range)
    {
        ArgumentNullException.ThrowIfNull(session);
        var layout = session.Layout;
        if (layout.ParagraphLineRanges.TryGetValue(paragraphIndex, out range))
        {
            return true;
        }

        range = default;
        return false;
    }
}

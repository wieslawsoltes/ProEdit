using ProEdit.Documents;
using ProEdit.Editing;

namespace ProEdit.Word.Editor.Editing;

internal sealed class EditorParagraphApplier
{
    private readonly IEditorMutableSession _session;

    public EditorParagraphApplier(IEditorMutableSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool Apply(Action<ParagraphBlock> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        var paragraphCount = _session.GetParagraphCountFast();
        if (paragraphCount == 0)
        {
            return false;
        }

        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, paragraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, paragraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var changed = false;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.GetParagraphFast(i);
            apply(paragraph);
            changed = true;
        }

        if (changed)
        {
            _session.RefreshLayout();
        }

        return changed;
    }
}

using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.Editor.Editing;

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

        if (_session.Document.ParagraphCount == 0)
        {
            return false;
        }

        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        var changed = false;
        for (var i = startIndex; i <= endIndex; i++)
        {
            var paragraph = _session.Document.GetParagraph(i);
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

using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;

namespace ProEdit.Word.Editor;

public sealed class LegacyEditorSessionFactory : IEditorSessionFactory
{
    public IEditorMutableSession Create(ITextMeasurer measurer, Document? document = null)
    {
        return new EditorController(measurer, document);
    }
}

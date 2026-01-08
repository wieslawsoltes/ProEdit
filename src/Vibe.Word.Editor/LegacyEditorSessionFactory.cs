using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;

namespace Vibe.Word.Editor;

public sealed class LegacyEditorSessionFactory : IEditorSessionFactory
{
    public IEditorMutableSession Create(ITextMeasurer measurer, Document? document = null)
    {
        return new EditorController(measurer, document);
    }
}

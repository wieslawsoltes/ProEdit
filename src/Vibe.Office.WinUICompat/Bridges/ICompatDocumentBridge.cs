using Vibe.Office.Documents;
using Vibe.Office.WinUICompat.Documents;

namespace Vibe.Office.WinUICompat.Bridges;

public interface ICompatDocumentBridge
{
    Document ToEditorDocument(RichTextDocument source);

    RichTextDocument FromEditorDocument(Document source);

    void SyncFromEditor(Document source, RichTextDocument target);
}

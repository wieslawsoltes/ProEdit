using ProEdit.Documents;
using ProEdit.WinUICompat.Documents;

namespace ProEdit.WinUICompat.Bridges;

public interface ICompatDocumentBridge
{
    Document ToEditorDocument(RichTextDocument source);

    RichTextDocument FromEditorDocument(Document source);

    void SyncFromEditor(Document source, RichTextDocument target);
}

namespace ProEdit.WinUICompat.Bridges;

using ProEdit.WinUICompat.Documents;

public interface ICompatClipboardBridge
{
    bool TrySetPlainText(string text);

    bool TryGetPlainText(out string text);

    bool TrySetRichDocument(RichTextDocument document, string plainText);

    bool TryGetRichDocument(out RichTextDocument document);
}

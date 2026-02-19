namespace Vibe.Office.WinUICompat.Bridges;

using Vibe.Office.WinUICompat.Documents;

public interface ICompatClipboardBridge
{
    bool TrySetPlainText(string text);

    bool TryGetPlainText(out string text);

    bool TrySetRichDocument(RichTextDocument document, string plainText);

    bool TryGetRichDocument(out RichTextDocument document);
}

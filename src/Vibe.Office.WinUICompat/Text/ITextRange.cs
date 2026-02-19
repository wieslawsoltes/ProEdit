namespace Vibe.Office.WinUICompat.Text;

using Vibe.Office.WinUICompat.Documents;

public interface ITextRange
{
    int StartOffset { get; }

    int EndOffset { get; }

    string GetText();

    void SetText(string text);

    RichTextDocument GetDocument();

    bool SetDocument(RichTextDocument document);
}

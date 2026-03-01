namespace Vibe.Office.WinUICompat.Text;

using Vibe.Office.WinUICompat.Documents;

public interface ITextDocument
{
    ITextRange Selection { get; }

    int SelectionStartOffset { get; }

    int SelectionEndOffset { get; }

    string GetText();

    void SetText(string text);

    void SetDocument(RichTextDocument document);

    void SetSelection(int startOffset, int endOffset);

    bool Undo();

    bool Redo();

    ITextRange GetRange(int startOffset, int endOffset);

    RichTextDocument GetRangeDocument(int startOffset, int endOffset);

    bool ReplaceRange(int startOffset, int endOffset, RichTextDocument fragmentDocument);
}

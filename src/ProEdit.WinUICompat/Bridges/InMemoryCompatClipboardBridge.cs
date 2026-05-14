namespace ProEdit.WinUICompat.Bridges;

using ProEdit.WinUICompat.Converters;
using ProEdit.WinUICompat.Documents;

public sealed class InMemoryCompatClipboardBridge : ICompatClipboardBridge
{
    private static readonly object Gate = new();
    private static readonly CompatFlowDocumentConverter Converter = new();
    private static string _plainText = string.Empty;
    private static RichTextDocument? _richDocument;

    public bool TrySetPlainText(string text)
    {
        lock (Gate)
        {
            _plainText = text ?? string.Empty;
            _richDocument = null;
        }

        return true;
    }

    public bool TryGetPlainText(out string text)
    {
        lock (Gate)
        {
            text = _plainText;
        }

        return true;
    }

    public bool TrySetRichDocument(RichTextDocument document, string plainText)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (Gate)
        {
            _richDocument = CloneDocument(document);
            _plainText = plainText ?? string.Empty;
        }

        return true;
    }

    public bool TryGetRichDocument(out RichTextDocument document)
    {
        lock (Gate)
        {
            if (_richDocument is null)
            {
                document = new RichTextDocument();
                return false;
            }

            document = CloneDocument(_richDocument);
            return true;
        }
    }

    private static RichTextDocument CloneDocument(RichTextDocument source)
    {
        return Converter.FromFlowDocument(Converter.ToFlowDocument(source));
    }
}

using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Html.Ast;

namespace ProEdit.Html;

public static class HtmlDocumentConverter
{
    public static Document FromHtml(ReadOnlySpan<char> html, HtmlOptions? options = null)
    {
        var text = html.Length == 0 ? string.Empty : html.ToString();
        if (DocumentHtmlParser.TryParse(text, out var document))
        {
            return document;
        }

        return new Document();
    }

    public static string ToHtml(Document document, HtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var prettyPrint = options?.PrettyPrint == true;
        return ClipboardHtmlSerializer.ToHtml(document, prettyPrint);
    }

    public static HtmlDocument ParseAst(ReadOnlySpan<char> html, HtmlOptions? options = null, HtmlNodeIdProvider? idProvider = null)
    {
        var parser = new HtmlAstParser(options, idProvider);
        return parser.Parse(html);
    }

    public static string ToHtml(HtmlDocument document, HtmlOptions? options = null)
    {
        var serializer = new HtmlAstSerializer(options);
        return serializer.Serialize(document);
    }
}

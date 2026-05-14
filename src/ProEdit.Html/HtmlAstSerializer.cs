using System.Text;
using ProEdit.Html.Ast;

namespace ProEdit.Html;

public sealed class HtmlAstSerializer
{
    private readonly HtmlOptions _options;

    public HtmlAstSerializer(HtmlOptions? options = null)
    {
        _options = options ?? new HtmlOptions();
    }

    public string Serialize(HtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder(1024);
        foreach (var child in document.Children)
        {
            WriteNode(builder, child);
        }

        return builder.ToString();
    }

    private void WriteNode(StringBuilder builder, HtmlNode node)
    {
        switch (node)
        {
            case HtmlTextNode textNode:
                builder.Append(EscapeText(textNode.Text));
                break;
            case HtmlCommentNode commentNode:
                builder.Append("<!--").Append(commentNode.Text).Append("-->");
                break;
            case HtmlElementNode element:
                WriteElement(builder, element);
                break;
        }
    }

    private void WriteElement(StringBuilder builder, HtmlElementNode element)
    {
        if (!_options.AllowScripts && element.Name.Equals("script", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_options.AllowStyles && element.Name.Equals("style", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        builder.Append('<').Append(element.Name);
        foreach (var attribute in element.Attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Name))
            {
                continue;
            }

            builder.Append(' ').Append(attribute.Name);
            if (attribute.Value is not null)
            {
                builder.Append("=\"").Append(EscapeAttribute(attribute.Value)).Append('"');
            }
        }

        var isVoid = element.IsVoidElement || HtmlVoidElements.IsVoid(element.Name);
        if (isVoid)
        {
            builder.Append(" />");
            return;
        }

        builder.Append('>');
        foreach (var child in element.Children)
        {
            WriteNode(builder, child);
        }

        builder.Append("</").Append(element.Name).Append('>');
    }

    private static string EscapeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string EscapeAttribute(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}

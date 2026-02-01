using Vibe.Office.Documents;

namespace Vibe.Office.Markdown;

internal static class MarkdownMetadata
{
    internal const string NamespaceUri = "vibe:markdown";
    internal const string Prefix = "md";

    internal const string BlockQuote = "blockquote";
    internal const string CodeBlock = "codeblock";
    internal const string ThematicBreak = "thematicbreak";
    internal const string CodeSpan = "codespan";
    internal const string Image = "image";
    internal const string TaskList = "tasklist";
    internal const string HtmlBlock = "htmlblock";
    internal const string HtmlInline = "htmlinline";

    internal const string AttrInfo = "info";
    internal const string AttrFence = "fence";
    internal const string AttrUrl = "url";
    internal const string AttrTitle = "title";
    internal const string AttrChecked = "checked";
    internal const string AttrHtml = "html";

    internal static MetadataContainer CreateHtmlContainer(string localName, string html)
    {
        var element = new MetadataElement(Prefix, localName, NamespaceUri)
        {
            Text = html ?? string.Empty
        };

        return new MetadataContainer(element);
    }

    internal static MetadataContainer CreateContainer(string localName)
    {
        var element = new MetadataElement(Prefix, localName, NamespaceUri);
        return new MetadataContainer(element);
    }

    internal static MetadataContainer CreateContainer(string localName, MetadataAttribute attribute)
    {
        var element = new MetadataElement(Prefix, localName, NamespaceUri);
        element.Attributes.Add(attribute);
        return new MetadataContainer(element);
    }

    internal static MetadataContainer CreateContainer(string localName, IReadOnlyList<MetadataAttribute> attributes)
    {
        var element = new MetadataElement(Prefix, localName, NamespaceUri);
        foreach (var attribute in attributes)
        {
            element.Attributes.Add(attribute);
        }

        return new MetadataContainer(element);
    }

    internal static bool IsMarkdownMetadata(MetadataContainer container, string localName)
    {
        if (container is null)
        {
            return false;
        }

        var element = container.Element;
        return string.Equals(element.NamespaceUri, NamespaceUri, StringComparison.Ordinal)
               && string.Equals(element.LocalName, localName, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? GetAttribute(MetadataContainer container, string localName)
    {
        var element = container.Element;
        for (var i = 0; i < element.Attributes.Count; i++)
        {
            var attribute = element.Attributes[i];
            if (string.Equals(attribute.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    internal static MetadataAttribute Attribute(string localName, string value)
    {
        return new MetadataAttribute(Prefix, localName, NamespaceUri, value);
    }
}

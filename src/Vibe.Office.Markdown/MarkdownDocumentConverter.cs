using Vibe.Office.Documents;

namespace Vibe.Office.Markdown;

public static class MarkdownDocumentConverter
{
    public static Document FromMarkdown(ReadOnlySpan<char> text, MarkdownOptions? options = null)
    {
        var parser = new MarkdownParser(options);
        var ast = parser.Parse(text);
        return MarkdownAstConverter.ToDocument(ast, options);
    }

    public static string ToMarkdown(Document document, MarkdownOptions? options = null)
    {
        return ToMarkdown(document, options, report: null);
    }

    public static string ToMarkdown(Document document, MarkdownOptions? options, MarkdownConversionReport? report)
    {
        ArgumentNullException.ThrowIfNull(document);
        var effectiveOptions = options ?? new MarkdownOptions();
        var downgrade = MarkdownDowngradePass.Apply(document, effectiveOptions, report);
        var ast = MarkdownAstConverter.FromDocument(downgrade.Document, effectiveOptions);
        var serializer = new MarkdownSerializer(effectiveOptions);
        return serializer.Serialize(ast);
    }
}

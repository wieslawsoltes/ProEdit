namespace Vibe.Office.Markdown;

internal static class MarkdownStyleIds
{
    internal const string Normal = "Normal";
    internal const string BlockQuote = "BlockQuote";
    internal const string CodeBlock = "CodeBlock";
    internal const string CodeInline = "CodeInline";
    internal const string ListParagraph = "ListParagraph";
    internal const string MarkdownTable = "MarkdownTable";
    internal const string TableCell = "TableCell";
    internal const string TableHeader = "TableHeader";
    internal const string Hyperlink = "Hyperlink";

    internal static string Heading(int level) => $"Heading{level}";
}

using System.Text;
using ProEdit.Documents;
using ProEdit.Markdown.Ast;

namespace ProEdit.Markdown.Tests;

internal static class MarkdownTestHelpers
{
    public static string GetInlineText(IReadOnlyList<MarkdownInline> inlines)
    {
        if (inlines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text:
                    builder.Append(text.Text);
                    break;
                case MarkdownCodeInline code:
                    builder.Append(code.Code);
                    break;
                case MarkdownEmphasisInline emphasis:
                    builder.Append(GetInlineText(emphasis.Inlines));
                    break;
                case MarkdownStrikethroughInline strike:
                    builder.Append(GetInlineText(strike.Inlines));
                    break;
                case MarkdownLinkInline link:
                    builder.Append(GetInlineText(link.Inlines));
                    break;
                case MarkdownImageInline image:
                    builder.Append(GetInlineText(image.AltText));
                    break;
                case MarkdownSoftBreakInline:
                case MarkdownHardBreakInline:
                    builder.Append('\n');
                    break;
            }
        }

        return builder.ToString();
    }

    public static string GetParagraphText(ParagraphBlock paragraph)
    {
        if (paragraph.Inlines.Count == 0)
        {
            return paragraph.Text ?? string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case RunInline run:
                    builder.Append(run.Text.GetText());
                    break;
                case MetadataStartInline:
                case MetadataEndInline:
                    break;
                case ImageInline:
                case ShapeInline:
                case ChartInline:
                case EquationInline:
                case PageNumberInline:
                case TotalPagesInline:
                    builder.Append(DocumentConstants.ObjectReplacementChar);
                    break;
            }
        }

        return builder.ToString();
    }
}

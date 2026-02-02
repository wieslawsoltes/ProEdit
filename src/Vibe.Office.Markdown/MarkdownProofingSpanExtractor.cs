using Vibe.Office.Editing;
using Vibe.Office.Html;
using Vibe.Office.Markdown.Ast;

namespace Vibe.Office.Markdown;

public static class MarkdownProofingSpanExtractor
{
    public static IReadOnlyList<ProofingSourceSpan> ExtractTextSpans(string markdown, MarkdownOptions? options = null)
    {
        var parser = new MarkdownParser(options);
        var document = parser.Parse(markdown.AsSpan());
        return ExtractTextSpans(document, options);
    }

    public static IReadOnlyList<ProofingSourceSpan> ExtractTextSpans(MarkdownDocument document, MarkdownOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var spans = new List<ProofingSourceSpan>();
        var htmlOptions = new HtmlOptions
        {
            NormalizeLineEndings = options?.NormalizeLineEndings ?? true
        };

        foreach (var block in document.Blocks)
        {
            CollectBlock(block, spans, options, htmlOptions);
        }

        return spans;
    }

    private static void CollectBlock(
        MarkdownBlock block,
        List<ProofingSourceSpan> spans,
        MarkdownOptions? options,
        HtmlOptions htmlOptions)
    {
        switch (block)
        {
            case MarkdownParagraphBlock paragraph:
                CollectInlines(paragraph.Inlines, spans, options, htmlOptions);
                break;
            case MarkdownHeadingBlock heading:
                CollectInlines(heading.Inlines, spans, options, htmlOptions);
                break;
            case MarkdownBlockQuoteBlock quote:
                foreach (var child in quote.Blocks)
                {
                    CollectBlock(child, spans, options, htmlOptions);
                }
                break;
            case MarkdownListBlock list:
                foreach (var item in list.Items)
                {
                    CollectBlock(item, spans, options, htmlOptions);
                }
                break;
            case MarkdownListItemBlock item:
                foreach (var child in item.Blocks)
                {
                    CollectBlock(child, spans, options, htmlOptions);
                }
                break;
            case MarkdownTableBlock table:
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        CollectInlines(cell.Inlines, spans, options, htmlOptions);
                    }
                }
                break;
            case MarkdownHtmlBlock htmlBlock:
                CollectHtml(htmlBlock.Html, htmlBlock.Span, spans, options, htmlOptions, isInline: false);
                break;
            case MarkdownCodeBlock:
            case MarkdownThematicBreakBlock:
                break;
        }
    }

    private static void CollectInlines(
        IReadOnlyList<MarkdownInline> inlines,
        List<ProofingSourceSpan> spans,
        MarkdownOptions? options,
        HtmlOptions htmlOptions)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline textInline:
                    AddSpan(textInline.Span, textInline.Text, spans);
                    break;
                case MarkdownEmphasisInline emphasis:
                    CollectInlines(emphasis.Inlines, spans, options, htmlOptions);
                    break;
                case MarkdownStrikethroughInline strike:
                    CollectInlines(strike.Inlines, spans, options, htmlOptions);
                    break;
                case MarkdownLinkInline link:
                    CollectInlines(link.Inlines, spans, options, htmlOptions);
                    break;
                case MarkdownImageInline image:
                    CollectInlines(image.AltText, spans, options, htmlOptions);
                    break;
                case MarkdownHtmlInline htmlInline:
                    CollectHtml(htmlInline.Html, htmlInline.Span, spans, options, htmlOptions, isInline: true);
                    break;
                case MarkdownCodeInline:
                case MarkdownHardBreakInline:
                case MarkdownSoftBreakInline:
                    break;
            }
        }
    }

    private static void CollectHtml(
        string html,
        MarkdownTextSpan markdownSpan,
        List<ProofingSourceSpan> spans,
        MarkdownOptions? options,
        HtmlOptions htmlOptions,
        bool isInline)
    {
        if (string.IsNullOrEmpty(html))
        {
            return;
        }

        if (markdownSpan.Start < 0 || markdownSpan.Length <= 0)
        {
            return;
        }

        var allowHtml = isInline
            ? options?.AllowHtmlInlines ?? true
            : options?.AllowHtmlBlocks ?? true;

        if (!allowHtml)
        {
            AddSpan(markdownSpan, html, spans);
            return;
        }

        var htmlSpans = HtmlProofingSpanExtractor.ExtractTextSpans(html, htmlOptions);
        foreach (var htmlSpan in htmlSpans)
        {
            spans.Add(new ProofingSourceSpan(markdownSpan.Start + htmlSpan.Start, htmlSpan.Length, htmlSpan.Text));
        }
    }

    private static void AddSpan(MarkdownTextSpan span, string text, List<ProofingSourceSpan> spans)
    {
        if (span.Start < 0 || span.Length <= 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        spans.Add(new ProofingSourceSpan(span.Start, span.Length, text));
    }
}

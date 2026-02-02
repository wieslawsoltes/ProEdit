using Vibe.Office.Documents;
using Vibe.Office.Markdown.Ast;
using Xunit;

namespace Vibe.Office.Markdown.Tests;

public class MarkdownHtmlInteropTests
{
    [Fact]
    public void ToDocument_AllowsHtmlBlock_ParsesTable()
    {
        var options = new MarkdownOptions { AllowHtmlBlocks = true, AllowHtmlInlines = true };
        var provider = new MarkdownNodeIdProvider();
        var document = new MarkdownDocument(provider.NextId(), MarkdownTextSpan.Unknown);
        document.Blocks.Add(new MarkdownHtmlBlock(provider.NextId(), MarkdownTextSpan.Unknown)
        {
            Html = "<table><tr><td>A</td><td>B</td></tr></table>"
        });

        var result = MarkdownAstConverter.ToDocument(document, options);

        Assert.Contains(result.Blocks, block => block is TableBlock);
    }

    [Fact]
    public void ToDocument_DisallowHtmlBlock_UsesPlainText()
    {
        var options = new MarkdownOptions { AllowHtmlBlocks = false, AllowHtmlInlines = false };
        var provider = new MarkdownNodeIdProvider();
        var document = new MarkdownDocument(provider.NextId(), MarkdownTextSpan.Unknown);
        document.Blocks.Add(new MarkdownHtmlBlock(provider.NextId(), MarkdownTextSpan.Unknown)
        {
            Html = "<table><tr><td>A</td></tr></table>"
        });

        var result = MarkdownAstConverter.ToDocument(document, options);

        var paragraph = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        Assert.Contains("<table", paragraph.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToDocument_AllowsHtmlInline_ParsesBold()
    {
        var options = new MarkdownOptions { AllowHtmlBlocks = true, AllowHtmlInlines = true };
        var provider = new MarkdownNodeIdProvider();
        var document = new MarkdownDocument(provider.NextId(), MarkdownTextSpan.Unknown);
        var paragraph = new MarkdownParagraphBlock(provider.NextId(), MarkdownTextSpan.Unknown);
        paragraph.Inlines.Add(new MarkdownHtmlInline(provider.NextId(), MarkdownTextSpan.Unknown, "<strong>Bold</strong>"));
        document.Blocks.Add(paragraph);

        var result = MarkdownAstConverter.ToDocument(document, options);

        var resultParagraph = Assert.IsType<ParagraphBlock>(result.Blocks[0]);
        Assert.Contains(resultParagraph.Inlines, inline =>
        {
            if (inline is not RunInline run || run.Style is null)
            {
                return false;
            }

            return run.Style.FontWeight == DocFontWeight.Bold;
        });
    }
}

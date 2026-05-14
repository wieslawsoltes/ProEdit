using ProEdit.Markdown;
using ProEdit.Markdown.Ast;
using Xunit;

namespace ProEdit.Markdown.Tests;

public class MarkdownProofingSpanExtractorTests
{
    [Fact]
    public void ExtractTextSpans_CollectsTextNodes()
    {
        var markdown = "Hello **world**";

        var spans = MarkdownProofingSpanExtractor.ExtractTextSpans(markdown);

        Assert.Collection(spans,
            span =>
            {
                Assert.Equal(0, span.Start);
                Assert.Equal(6, span.Length);
                Assert.Equal("Hello ", span.Text);
            },
            span =>
            {
                Assert.Equal(8, span.Start);
                Assert.Equal(5, span.Length);
                Assert.Equal("world", span.Text);
            });
    }

    [Fact]
    public void ExtractTextSpans_CollectsHtmlInlineText()
    {
        var options = new MarkdownOptions { AllowHtmlInlines = true };
        var provider = new MarkdownNodeIdProvider();
        var document = new MarkdownDocument(provider.NextId(), MarkdownTextSpan.Unknown);
        var paragraph = new MarkdownParagraphBlock(provider.NextId(), MarkdownTextSpan.Unknown);

        paragraph.Inlines.Add(new MarkdownTextInline(provider.NextId(), new MarkdownTextSpan(0, 6), "Hello "));
        paragraph.Inlines.Add(new MarkdownHtmlInline(provider.NextId(), new MarkdownTextSpan(6, 18), "<span>world</span>"));
        document.Blocks.Add(paragraph);

        var spans = MarkdownProofingSpanExtractor.ExtractTextSpans(document, options);

        Assert.Collection(spans,
            span =>
            {
                Assert.Equal(0, span.Start);
                Assert.Equal(6, span.Length);
                Assert.Equal("Hello ", span.Text);
            },
            span =>
            {
                Assert.Equal(12, span.Start);
                Assert.Equal(5, span.Length);
                Assert.Equal("world", span.Text);
            });
    }
}

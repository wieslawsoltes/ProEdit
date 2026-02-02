using Vibe.Office.Html;
using Xunit;

namespace Vibe.Office.Html.Tests;

public class HtmlProofingSpanExtractorTests
{
    [Fact]
    public void ExtractTextSpans_CollectsTextNodesWithSpans()
    {
        var html = "<p>Hello <strong>world</strong></p>";

        var spans = HtmlProofingSpanExtractor.ExtractTextSpans(html);

        Assert.Collection(spans,
            span =>
            {
                Assert.Equal(3, span.Start);
                Assert.Equal(6, span.Length);
                Assert.Equal("Hello ", span.Text);
            },
            span =>
            {
                Assert.Equal(17, span.Start);
                Assert.Equal(5, span.Length);
                Assert.Equal("world", span.Text);
            });
    }
}

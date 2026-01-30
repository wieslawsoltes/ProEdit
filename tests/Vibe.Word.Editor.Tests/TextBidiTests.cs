using Vibe.Office.Layout;
using Xunit;

namespace Vibe.Word.Editor.Tests;

public sealed class TextBidiTests
{
    [Fact]
    public void GetBidiSpans_UsesIsolateEmbeddingLevels()
    {
        var text = "abc \u2067DEF\u2069 ghi";
        var spans = TextBidi.GetBidiSpans(text.AsSpan(), false);

        Assert.Equal(3, spans.Count);
        Assert.Equal(new BidiSpan(0, 5, 0), spans[0]);
        Assert.Equal(new BidiSpan(5, 3, 2), spans[1]);
        Assert.Equal(new BidiSpan(8, 5, 0), spans[2]);
    }

    [Fact]
    public void GetBidiSpans_ResolvesBracketsInRtlParagraph()
    {
        var text = "\u05d0(\u0061)\u05d1";
        var spans = TextBidi.GetBidiSpans(text.AsSpan(), true);

        Assert.Equal(3, spans.Count);
        Assert.Equal(new BidiSpan(0, 2, 1), spans[0]);
        Assert.Equal(new BidiSpan(2, 1, 2), spans[1]);
        Assert.Equal(new BidiSpan(3, 2, 1), spans[2]);
    }
}

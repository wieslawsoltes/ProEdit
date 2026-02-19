using Vibe.Office.WinUICompat.Bridges;
using Vibe.Office.WinUICompat.Documents;
using Xunit;

namespace Vibe.Office.WinUICompat.Tests;

public sealed class CompatDocumentContinuationLayoutTests
{
    [Fact]
    public void SegmentByMaxLines_ProducesStableContinuation()
    {
        var source = CreateParagraphDocument("one", "two", "three", "four", "five");
        var continuation = new CompatDocumentContinuationLayout();

        continuation.UpdateSource(source, viewportWidth: 600f);
        var first = continuation.GetSegmentByMaxLines(0, 2);
        var second = continuation.GetSegmentByMaxLines(first.EndLineIndex, 2);
        var third = continuation.GetRemainingSegment(second.EndLineIndex);

        Assert.Equal(0, first.StartLineIndex);
        Assert.Equal(2, first.LineCount);
        Assert.True(first.HasOverflow);

        Assert.Equal(first.EndLineIndex, second.StartLineIndex);
        Assert.Equal(2, second.LineCount);
        Assert.True(second.HasOverflow);

        Assert.Equal(second.EndLineIndex, third.StartLineIndex);
        Assert.True(third.LineCount >= 1);
        Assert.False(third.HasOverflow);
    }

    [Fact]
    public void SegmentByHeight_AlwaysAdvancesAtLeastOneLine()
    {
        var source = CreateParagraphDocument("alpha", "beta", "gamma");
        var continuation = new CompatDocumentContinuationLayout();
        continuation.UpdateSource(source, viewportWidth: 600f);

        var first = continuation.GetSegmentByHeight(0, viewportHeight: 1f);
        var second = continuation.GetSegmentByHeight(first.EndLineIndex, viewportHeight: 1f);

        Assert.True(first.LineCount >= 1);
        Assert.True(second.StartLineIndex >= first.EndLineIndex);
    }

    [Fact]
    public void SegmentBeyondEnd_ReturnsEmptyWithoutOverflow()
    {
        var source = CreateParagraphDocument("single");
        var continuation = new CompatDocumentContinuationLayout();
        continuation.UpdateSource(source, viewportWidth: 600f);

        var segment = continuation.GetRemainingSegment(continuation.LineCount + 5);

        Assert.True(segment.IsEmpty);
        Assert.False(segment.HasOverflow);
    }

    private static RichTextDocument CreateParagraphDocument(params string[] lines)
    {
        var document = new RichTextDocument();
        for (var i = 0; i < lines.Length; i++)
        {
            document.Blocks.Add(new Paragraph(lines[i]));
        }

        return document;
    }
}

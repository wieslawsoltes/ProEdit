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

    [Fact]
    public void SegmentByHeight_ExtendsForFloatingObjectsAnchoredInSegment()
    {
        var source = new RichTextDocument();
        var paragraph = new Paragraph();
        var figure = new Figure
        {
            Width = 220,
            Height = 180,
            HorizontalAnchor = "PageLeft"
        };
        figure.Blocks.Add(new Paragraph("floating block"));
        paragraph.Inlines.Add(figure);
        paragraph.Inlines.Add(new Run("Body text after floating figure."));
        source.Blocks.Add(paragraph);
        source.Blocks.Add(new Paragraph("Trailing paragraph for continuation."));

        var continuation = new CompatDocumentContinuationLayout();
        continuation.UpdateSource(source, viewportWidth: 600f);

        var floatingCount =
            continuation.RenderDocument.EditorLayout.FloatingObjects.Count
            + continuation.RenderDocument.EditorLayout.ExtraFloatingObjects.Count;
        Assert.True(floatingCount > 0);

        var first = continuation.GetSegmentByHeight(0, viewportHeight: 24f);
        Assert.True(first.LineCount >= 1);
        Assert.True(first.Height > 24f);
    }

    [Fact]
    public void SegmentByMaxLines_IncludesFloatingExtentInHeight()
    {
        var source = new RichTextDocument();
        var paragraph = new Paragraph();
        var figure = new Figure
        {
            Width = 200,
            Height = 160,
            HorizontalAnchor = "PageLeft"
        };
        figure.Blocks.Add(new Paragraph("float"));
        paragraph.Inlines.Add(figure);
        paragraph.Inlines.Add(new Run("Caption text."));
        source.Blocks.Add(paragraph);
        source.Blocks.Add(new Paragraph("Second paragraph."));

        var continuation = new CompatDocumentContinuationLayout();
        continuation.UpdateSource(source, viewportWidth: 600f);

        var first = continuation.GetSegmentByMaxLines(0, maxLines: 1);
        var firstLine = continuation.Lines[first.StartLineIndex];
        Assert.True(first.Height >= firstLine.LineHeight);

        var floatingCount =
            continuation.RenderDocument.EditorLayout.FloatingObjects.Count
            + continuation.RenderDocument.EditorLayout.ExtraFloatingObjects.Count;
        Assert.True(floatingCount > 0);
        Assert.True(first.Height > firstLine.LineHeight + 0.5f);
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

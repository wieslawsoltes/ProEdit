using Vibe.Office.FlowDocument;
using Xunit;

namespace Vibe.Office.FlowDocument.Tests;

public sealed class FlowElementCollectionTests
{
    [Fact]
    public void InlineCollectionTracksParent()
    {
        var paragraph = new Paragraph();
        var run = new Run("Hello");

        paragraph.Inlines.Add(run);

        Assert.Same(paragraph, run.Parent);

        paragraph.Inlines.Remove(run);

        Assert.Null(run.Parent);
    }

    [Fact]
    public void BlockCollectionTracksParent()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();

        document.Blocks.Add(paragraph);

        Assert.Same(document, paragraph.Parent);

        document.Blocks.Remove(paragraph);

        Assert.Null(paragraph.Parent);
    }
}

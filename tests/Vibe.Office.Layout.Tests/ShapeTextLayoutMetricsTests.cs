using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;
using Xunit;

namespace Vibe.Office.Layout.Tests;

public sealed class ShapeTextLayoutMetricsTests
{
    [Fact]
    public void ComputeMetrics_TopAlignment_NoScale()
    {
        var layout = BuildLayout("Hello", 120f, 40f);
        var textBox = new ShapeTextBox
        {
            Properties =
            {
                AutoFit = ShapeTextAutoFit.None,
                VerticalAlignment = ShapeTextVerticalAlignment.Top
            }
        };
        var textBounds = new DocRect(10f, 20f, 100f, 30f);

        Assert.True(ShapeTextLayoutHelper.TryComputeMetrics(layout, textBox, textBounds, out var metrics));
        Assert.Equal(textBounds.X, metrics.OriginX);
        Assert.Equal(textBounds.Y, metrics.OriginY);
        Assert.Equal(1f, metrics.Scale);
        Assert.Equal(textBounds, metrics.TextBounds);
    }

    [Fact]
    public void ComputeMetrics_TextToFitShape_ScalesDown()
    {
        var layout = BuildLayout("Hello", 100f, 20f);
        var textBox = new ShapeTextBox
        {
            Properties =
            {
                AutoFit = ShapeTextAutoFit.TextToFitShape,
                VerticalAlignment = ShapeTextVerticalAlignment.Top
            }
        };
        var textBounds = new DocRect(0f, 0f, 2f, 10f);

        Assert.True(ShapeTextLayoutHelper.TryComputeMetrics(layout, textBox, textBounds, out var metrics));
        Assert.InRange(metrics.Scale, 0.39f, 0.41f);
    }

    [Fact]
    public void ComputeMetrics_VerticalAlignmentCenter_AdjustsOrigin()
    {
        var layout = BuildLayout("Hello", 100f, 20f);
        var textBox = new ShapeTextBox
        {
            Properties =
            {
                AutoFit = ShapeTextAutoFit.None,
                VerticalAlignment = ShapeTextVerticalAlignment.Center
            }
        };
        var textBounds = new DocRect(0f, 20f, 100f, 30f);

        Assert.True(ShapeTextLayoutHelper.TryComputeMetrics(layout, textBox, textBounds, out var metrics));
        Assert.Equal(30f, metrics.OriginY);
    }

    private static DocumentLayout BuildLayout(string text, float width, float height)
    {
        var document = new Document();
        document.Blocks.Clear();
        var paragraph = new ParagraphBlock(text);
        paragraph.Inlines.Add(new RunInline(text));
        document.Blocks.Add(paragraph);

        var settings = new LayoutSettings
        {
            UsePagination = false,
            PageWidth = width,
            PageHeight = height,
            ViewportWidth = width,
            ViewportHeight = height,
            PageGap = 0f,
            MarginLeft = 0f,
            MarginRight = 0f,
            MarginTop = 0f,
            MarginBottom = 0f,
            HeaderOffset = 0f,
            FooterOffset = 0f,
            Gutter = 0f
        };

        return new DocumentLayouter().Layout(document, settings, new TestTextMeasurer());
    }
}

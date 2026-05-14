using ProEdit.Documents;
using ProEdit.Layout;
using ProEdit.Primitives;
using Xunit;

namespace ProEdit.Layout.Tests;

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

    [Fact]
    public void ComputeMetrics_PreservesPositiveContentOffsets()
    {
        var document = new Document();
        document.Blocks.Clear();
        var paragraph = new ParagraphBlock("Hello");
        paragraph.Properties.IndentLeft = 12f;
        paragraph.Properties.SpacingBefore = 8f;
        paragraph.Inlines.Add(new RunInline("Hello"));
        document.Blocks.Add(paragraph);

        var settings = new LayoutSettings
        {
            UsePagination = false,
            PageWidth = 160f,
            PageHeight = 60f,
            ViewportWidth = 160f,
            ViewportHeight = 60f,
            PageGap = 0f,
            MarginLeft = 0f,
            MarginRight = 0f,
            MarginTop = 0f,
            MarginBottom = 0f,
            HeaderOffset = 0f,
            FooterOffset = 0f,
            Gutter = 0f
        };

        var layout = new DocumentLayouter().Layout(document, settings, new TestTextMeasurer());
        var textBox = new ShapeTextBox
        {
            Properties =
            {
                AutoFit = ShapeTextAutoFit.None,
                VerticalAlignment = ShapeTextVerticalAlignment.Top
            }
        };
        var textBounds = new DocRect(20f, 30f, 100f, 20f);

        Assert.True(ShapeTextLayoutHelper.TryComputeMetrics(layout, textBox, textBounds, out var metrics));
        Assert.True(metrics.ContentBounds.X > 0f);
        Assert.True(metrics.ContentBounds.Y > 0f);
        Assert.Equal(textBounds.X, metrics.OriginX, 3);
        Assert.Equal(textBounds.Y, metrics.OriginY, 3);
    }

    [Fact]
    public void ComputeMetrics_PreservesRightAlignedLineOffsets()
    {
        var document = new Document();
        document.Blocks.Clear();
        var paragraph = new ParagraphBlock("Hello");
        paragraph.Properties.Alignment = ParagraphAlignment.Right;
        paragraph.Inlines.Add(new RunInline("Hello"));
        document.Blocks.Add(paragraph);

        var settings = new LayoutSettings
        {
            UsePagination = false,
            PageWidth = 160f,
            PageHeight = 60f,
            ViewportWidth = 160f,
            ViewportHeight = 60f,
            PageGap = 0f,
            MarginLeft = 0f,
            MarginRight = 0f,
            MarginTop = 0f,
            MarginBottom = 0f,
            HeaderOffset = 0f,
            FooterOffset = 0f,
            Gutter = 0f
        };

        var layout = new DocumentLayouter().Layout(document, settings, new TestTextMeasurer());
        var textBox = new ShapeTextBox
        {
            Properties =
            {
                AutoFit = ShapeTextAutoFit.None,
                VerticalAlignment = ShapeTextVerticalAlignment.Top
            }
        };
        var textBounds = new DocRect(24f, 12f, 100f, 20f);

        Assert.True(ShapeTextLayoutHelper.TryComputeMetrics(layout, textBox, textBounds, out var metrics));
        Assert.True(metrics.ContentBounds.X > 0f);
        Assert.Equal(textBounds.X, metrics.OriginX, 3);
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

using System;
using System.Linq;
using ProEdit.Documents;
using ProEdit.Layout;
using Xunit;

namespace ProEdit.Layout.Tests;

public sealed class JustificationTests
{
    [Fact]
    public void JustifiedParagraph_StretchesNonLastLine()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Compatibility.UseWord97LineBreakRules = true;

        var paragraph = new ParagraphBlock("one two three four");
        paragraph.Properties.Alignment = ParagraphAlignment.Justify;
        document.Blocks.Add(paragraph);

        var settings = new LayoutSettings
        {
            UsePagination = false,
            ViewportWidth = 12f,
            ViewportHeight = 200f,
            MarginLeft = 0f,
            MarginRight = 0f,
            MarginTop = 0f,
            MarginBottom = 0f
        };

        var layout = new DocumentLayouter().Layout(document, settings, new TestTextMeasurer());
        var lines = layout.Lines.Where(line => line.ParagraphIndex == 0).ToList();

        Assert.True(lines.Count >= 2);
        var availableWidth = layout.Pages[0].ContentBounds.Width;
        Assert.InRange(MathF.Abs(lines[0].Width - availableWidth), 0f, 0.01f);
        Assert.True(lines[^1].Width < availableWidth);
    }
}

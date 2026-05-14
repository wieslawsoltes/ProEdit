using System;
using ProEdit.Documents;
using ProEdit.Layout;
using Xunit;

namespace ProEdit.Word.Editor.Tests;

public sealed class HeaderFooterTableLayoutTests
{
    [Fact]
    public void HeaderFooterTables_AreIncludedInLayout()
    {
        var document = new Document();
        document.Header.Blocks.Clear();
        document.Header.Blocks.Add(BuildHeaderTable());

        var layouter = new DocumentLayouter();
        var layout = layouter.Layout(document, new LayoutSettings(), new TestTextMeasurer());

        Assert.NotEmpty(layout.HeaderFooters);
        var headerFooter = layout.HeaderFooters[0];
        Assert.NotEmpty(headerFooter.HeaderTables);
        var table = headerFooter.HeaderTables[0];
        Assert.True(table.Bounds.Width > 0f);
        Assert.True(table.Bounds.Height > 0f);
    }

    private static TableBlock BuildHeaderTable()
    {
        var paragraph = new ParagraphBlock("Header Cell");
        var cell = new TableCell(new[] { paragraph });
        var row = new TableRow(new[] { cell });
        return new TableBlock(new[] { row });
    }

    private sealed class TestTextMeasurer : ITextMeasurer
    {
        public TextMetrics MeasureText(string text, TextStyle style)
        {
            var length = string.IsNullOrEmpty(text) ? 0 : text.Length;
            var height = MathF.Max(1f, style.FontSize);
            var ascent = height * 0.8f;
            var descent = MathF.Max(1f, height - ascent);
            return new TextMetrics(length * height * 0.5f, height, ascent, descent);
        }
    }
}

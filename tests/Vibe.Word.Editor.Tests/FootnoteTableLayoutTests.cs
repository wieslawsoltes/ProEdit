using System;
using System.Linq;
using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Xunit;

namespace Vibe.Word.Editor.Tests;

public sealed class FootnoteTableLayoutTests
{
    [Fact]
    public void FootnoteTables_AreIncludedInLayout()
    {
        var document = new Document();
        var paragraph = document.Blocks.OfType<ParagraphBlock>().First();
        paragraph.Text = string.Empty;
        paragraph.Inlines.Add(new RunInline("Body"));
        paragraph.Inlines.Add(new FootnoteReferenceInline(1));

        var footnote = new FootnoteDefinition(1);
        footnote.Blocks.Add(BuildFootnoteTable());
        document.Footnotes[1] = footnote;

        var layouter = new DocumentLayouter();
        var layout = layouter.Layout(document, new LayoutSettings(), new TestTextMeasurer());

        Assert.NotEmpty(layout.Footnotes);
        var footnoteLayout = layout.Footnotes[0];
        Assert.NotEmpty(footnoteLayout.Tables);
        var table = footnoteLayout.Tables[0];
        Assert.True(table.Bounds.Width > 0f);
        Assert.True(table.Bounds.Height > 0f);
    }

    private static TableBlock BuildFootnoteTable()
    {
        var paragraph = new ParagraphBlock("Footnote Cell");
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

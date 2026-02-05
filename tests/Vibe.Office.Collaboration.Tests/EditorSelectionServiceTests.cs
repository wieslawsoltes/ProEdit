using Vibe.Office.Documents;
using Vibe.Office.Layout;
using Vibe.Word.Editor;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class EditorSelectionServiceTests
{
    [Fact]
    public void TryGetCaretPointForPositionReturnsPoint()
    {
        var document = new Document();
        document.Blocks.Add(new ParagraphBlock("Hello"));
        var controller = new EditorController(new TestTextMeasurer(), document);
        controller.UpdateLayout(800, 600);

        var result = controller.TryGetCaretPoint(new TextPosition(0, 3), out var point, out var lineIndex);

        Assert.True(result);
        Assert.True(lineIndex >= 0);
        Assert.True(point.X >= 0f);
    }

    private sealed class TestTextMeasurer : ITextMeasurer
    {
        public TextMetrics MeasureText(string text, TextStyle style)
        {
            var width = (text?.Length ?? 0) * 7f;
            return new TextMetrics(width, 12f, 9f, 3f);
        }
    }
}

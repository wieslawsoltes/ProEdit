using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Layout;
using ProEdit.Word.Editor;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class SingleUserBaselineTests
{
    [Fact]
    public async Task SnapshotUndoRedo_RemainsFunctionalInSingleUserMode()
    {
        var document = new Document();
        var measurer = new StubTextMeasurer();
        var session = new EditorController(measurer, document);
        var history = new EditorCommandHistory(session);
        var dispatcher = new EditorCommandDispatcher { History = history };

        dispatcher.Dispatch(new InsertTextCommand("Hello"), session);

        Assert.True(history.CanUndo);
        Assert.Equal("Hello", session.Document.GetParagraph(0).Text);

        await history.UndoAsync();

        Assert.False(history.CanUndo);
        Assert.Equal(string.Empty, session.Document.GetParagraph(0).Text);
    }

    private sealed class StubTextMeasurer : ITextMeasurer
    {
        public TextMetrics MeasureText(string text, TextStyle style)
        {
            var width = string.IsNullOrEmpty(text) ? 0f : text.Length * 5f;
            var height = style.FontSize <= 0f ? 10f : style.FontSize;
            return new TextMetrics(width, height, height * 0.8f, height * 0.2f);
        }
    }
}

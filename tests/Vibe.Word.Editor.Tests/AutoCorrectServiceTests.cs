using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Xunit;

namespace Vibe.Word.Editor.Tests;

public class AutoCorrectServiceTests
{
    [Fact]
    public void AutoCorrectService_ReplacesMisspelledWordOnWhitespace()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock());

        var session = new EditorController(new EditorTestTextMeasurer(), document);
        var service = AutoCorrectService.CreateDefault();

        session.InsertText("teh ");

        Assert.True(service.TryGetReplacement(session, " ".AsSpan(), out var replacement));
        Assert.Equal("the", replacement.Replacement);
        Assert.Equal(0, replacement.ParagraphIndex);
        Assert.Equal(0, replacement.StartOffset);
    }

    [Fact]
    public void AutoCorrectService_CollapsesDoubleSpace()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock());

        var session = new EditorController(new EditorTestTextMeasurer(), document);
        var service = AutoCorrectService.CreateDefault();

        session.InsertText("hello  ");

        Assert.True(service.TryGetReplacement(session, " ".AsSpan(), out var replacement));
        Assert.Equal(" ", replacement.Replacement);
        Assert.Equal(0, replacement.ParagraphIndex);
        Assert.Equal(5, replacement.StartOffset);
        Assert.Equal(2, replacement.Length);
    }
}

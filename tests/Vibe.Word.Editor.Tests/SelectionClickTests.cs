using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Xunit;

namespace Vibe.Word.Editor.Tests;

public class SelectionClickTests
{
    [Fact]
    public void SelectWordFromPoint_SelectsWordBoundaries()
    {
        var session = CreateSessionWithText("Hello world");
        session.UpdateLayout(200, 200);

        var position = new TextPosition(0, 7);
        session.SetSelection(new TextRange(position, position));
        Assert.True(session.TryGetCaretPoint(out var point, out _));

        Assert.True(session.TrySelectWordFromPoint(point.X, point.Y, SelectionUpdateMode.Replace));

        var selection = session.Selection.Normalize();
        Assert.Equal(new TextPosition(0, 6), selection.Start);
        Assert.Equal(new TextPosition(0, 11), selection.End);
    }

    [Fact]
    public void SelectParagraphFromPoint_SelectsEntireParagraph()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock());

        var session = new EditorController(new EditorTestTextMeasurer(), document);
        session.InsertText("First");
        session.InsertParagraphBreak();
        session.InsertText("Second line");
        session.UpdateLayout(300, 200);

        var position = new TextPosition(1, 3);
        session.SetSelection(new TextRange(position, position));
        Assert.True(session.TryGetCaretPoint(out var point, out _));

        Assert.True(session.TrySelectParagraphFromPoint(point.X, point.Y, SelectionUpdateMode.Replace));

        var paragraph = document.Blocks[1] as ParagraphBlock;
        Assert.NotNull(paragraph);

        var selection = session.Selection.Normalize();
        Assert.Equal(new TextPosition(1, 0), selection.Start);
        Assert.Equal(
            new TextPosition(1, DocumentEditHelpers.GetParagraphLength(paragraph!)),
            selection.End);
    }

    private static EditorController CreateSessionWithText(string text)
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock());

        var session = new EditorController(new EditorTestTextMeasurer(), document);
        session.InsertText(text);
        return session;
    }
}

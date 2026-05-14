using ProEdit.Documents;
using ProEdit.Editing;
using Xunit;

namespace ProEdit.Word.Editor.Tests;

public sealed class EditorCommandInputRouterTests
{
    [Fact]
    public async Task NavigationAndSelectAllCommands_DoNotCreateUndoEntries()
    {
        var session = CreateSessionWithText("Alpha");
        var dispatcher = new EditorCommandDispatcher();
        var services = new EditorServices();
        new BasicEditingModule().Register(new EditorModuleContext(services, dispatcher));

        var history = new EditorCommandHistory(session);
        dispatcher.History = history;
        var router = new EditorCommandInputRouter(dispatcher, session, history);

        Assert.False(history.CanUndo);

        Assert.True(router.HandleKey(EditorKey.End, EditorKeyEventKind.Down, EditorModifiers.None));
        Assert.True(router.HandleKey(EditorKey.Home, EditorKeyEventKind.Down, EditorModifiers.None));
        Assert.True(router.HandleKey(EditorKey.A, EditorKeyEventKind.Down, EditorModifiers.Control));
        Assert.False(history.CanUndo);

        Assert.True(router.HandleTextInput("!".AsSpan(), EditorModifiers.None));
        Assert.True(history.CanUndo);

        await history.UndoAsync();
        Assert.False(history.CanUndo);
        Assert.Equal("Alpha", GetParagraphText(session, 0));
    }

    [Fact]
    public async Task TabInsertion_IsSingleUndoableStep()
    {
        var session = CreateSessionWithText("A");
        var dispatcher = new EditorCommandDispatcher();
        var services = new EditorServices();
        new BasicEditingModule().Register(new EditorModuleContext(services, dispatcher));

        var history = new EditorCommandHistory(session);
        dispatcher.History = history;
        var router = new EditorCommandInputRouter(dispatcher, session, history, acceptsTabProvider: () => true);

        session.SetSelection(new TextRange(new TextPosition(0, 1), new TextPosition(0, 1)));

        Assert.True(router.HandleKey(EditorKey.Tab, EditorKeyEventKind.Down, EditorModifiers.None));
        Assert.Equal("A\t", GetParagraphText(session, 0));
        Assert.True(history.CanUndo);

        await history.UndoAsync();
        Assert.Equal("A", GetParagraphText(session, 0));
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        await history.RedoAsync();
        Assert.Equal("A\t", GetParagraphText(session, 0));
    }

    [Fact]
    public void EnterKey_IsIgnored_WhenAcceptsReturnDisabled()
    {
        var session = CreateSessionWithText("Alpha");
        var dispatcher = new EditorCommandDispatcher();
        var services = new EditorServices();
        new BasicEditingModule().Register(new EditorModuleContext(services, dispatcher));
        var history = new EditorCommandHistory(session);
        dispatcher.History = history;
        var router = new EditorCommandInputRouter(
            dispatcher,
            session,
            history,
            acceptsReturnProvider: () => false);

        session.SetSelection(new TextRange(new TextPosition(0, 5), new TextPosition(0, 5)));

        Assert.False(router.HandleKey(EditorKey.Enter, EditorKeyEventKind.Down, EditorModifiers.None));
        Assert.Equal(1, session.Document.ParagraphCount);
        Assert.Equal("Alpha", GetParagraphText(session, 0));
    }

    [Fact]
    public void MutatingInput_IsBlocked_WhenReadOnlyEnabled()
    {
        var session = CreateSessionWithText("Alpha");
        var dispatcher = new EditorCommandDispatcher();
        var services = new EditorServices();
        new BasicEditingModule().Register(new EditorModuleContext(services, dispatcher));
        var history = new EditorCommandHistory(session);
        dispatcher.History = history;
        var router = new EditorCommandInputRouter(
            dispatcher,
            session,
            history,
            acceptsTabProvider: () => true,
            acceptsReturnProvider: () => true,
            isReadOnlyProvider: () => true);

        session.SetSelection(new TextRange(new TextPosition(0, 5), new TextPosition(0, 5)));

        Assert.False(router.HandleTextInput("!".AsSpan(), EditorModifiers.None));
        Assert.False(router.HandleKey(EditorKey.Backspace, EditorKeyEventKind.Down, EditorModifiers.None));
        Assert.False(router.HandleKey(EditorKey.Delete, EditorKeyEventKind.Down, EditorModifiers.None));
        Assert.False(router.HandleKey(EditorKey.Enter, EditorKeyEventKind.Down, EditorModifiers.None));
        Assert.False(router.HandleKey(EditorKey.Tab, EditorKeyEventKind.Down, EditorModifiers.None));
        Assert.Equal("Alpha", GetParagraphText(session, 0));

        Assert.True(router.HandleKey(EditorKey.End, EditorKeyEventKind.Down, EditorModifiers.None));
        Assert.True(router.HandleKey(EditorKey.A, EditorKeyEventKind.Down, EditorModifiers.Control));
    }

    [Fact]
    public void EnterTextInput_Crlf_IsHandledUsingParagraphBreak()
    {
        var session = CreateSessionWithText("Alpha");
        var dispatcher = new EditorCommandDispatcher();
        var services = new EditorServices();
        new BasicEditingModule().Register(new EditorModuleContext(services, dispatcher));
        var history = new EditorCommandHistory(session);
        dispatcher.History = history;
        var router = new EditorCommandInputRouter(
            dispatcher,
            session,
            history,
            acceptsReturnProvider: () => true);

        session.SetSelection(new TextRange(new TextPosition(0, 5), new TextPosition(0, 5)));

        Assert.True(router.HandleTextInput("\r\n".AsSpan(), EditorModifiers.None));
        Assert.Equal(2, session.Document.ParagraphCount);
        Assert.Equal("Alpha", GetParagraphText(session, 0));
    }

    [Fact]
    public void UndoRedoShortcuts_AreIgnoredWithoutHistoryEntries()
    {
        var session = CreateSessionWithText("Alpha");
        var dispatcher = new EditorCommandDispatcher();
        var services = new EditorServices();
        new BasicEditingModule().Register(new EditorModuleContext(services, dispatcher));
        var history = new EditorCommandHistory(session);
        dispatcher.History = history;
        var router = new EditorCommandInputRouter(dispatcher, session, history);

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(router.HandleKey(EditorKey.Z, EditorKeyEventKind.Down, EditorModifiers.Control));
        Assert.False(router.HandleKey(EditorKey.Y, EditorKeyEventKind.Down, EditorModifiers.Control));
        Assert.False(router.HandleKey(EditorKey.Z, EditorKeyEventKind.Down, EditorModifiers.Control | EditorModifiers.Shift));
    }

    private static EditorController CreateSessionWithText(string text)
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock());

        var session = new EditorController(new EditorTestTextMeasurer(), document);
        session.InsertText(text);
        session.UpdateLayout(800, 600);
        return session;
    }

    private static string GetParagraphText(EditorController session, int index)
    {
        return DocumentEditHelpers.GetParagraphText(session.Document.GetParagraph(index));
    }
}

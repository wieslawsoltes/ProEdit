using System.Linq;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Word.Editor;
using ProEdit.Word.Editor.Editing;
using Xunit;

namespace ProEdit.Word.Editor.Tests;

public sealed class CommentEditingTests
{
    [Fact]
    public async Task NewComment_InsertsMarkersAndDefinition()
    {
        var (session, router) = CreateReviewSession("Hello world");
        session.SetSelection(new TextRange(new TextPosition(0, 0), new TextPosition(0, 5)));

        Assert.True(await router.ExecuteAsync(EditorReviewCommandIds.Comments.NewComment));

        var comment = Assert.Single(session.Document.Comments);
        var commentId = comment.Key;
        var paragraph = session.Document.GetParagraph(0);
        var expectedText = $"Hello{commentId} world";
        Assert.Equal(expectedText, DocumentEditHelpers.GetParagraphText(paragraph));
        Assert.Contains(paragraph.Inlines, inline => inline is CommentRangeStartInline start && start.Id == commentId);
        Assert.Contains(paragraph.Inlines, inline => inline is CommentRangeEndInline end && end.Id == commentId);
        Assert.Contains(paragraph.Inlines, inline => inline is CommentReferenceInline reference && reference.Id == commentId);
        Assert.False(string.IsNullOrWhiteSpace(comment.Value.Author));
    }

    [Fact]
    public async Task ReplyAndResolve_UpdateThreadState()
    {
        var (session, router) = CreateReviewSession("Hello world");
        session.SetSelection(new TextRange(new TextPosition(0, 0), new TextPosition(0, 5)));
        await router.ExecuteAsync(EditorReviewCommandIds.Comments.NewComment);

        var rootId = session.Document.Comments.Keys.Single();
        var root = session.Document.Comments[rootId];

        Assert.True(await router.ExecuteAsync(EditorReviewCommandIds.Comments.ResolveComment, rootId));
        Assert.True(root.IsResolved);
        Assert.NotNull(root.ResolvedDate);

        Assert.True(await router.ExecuteAsync(EditorReviewCommandIds.Comments.ReplyComment, rootId));
        Assert.Equal(2, session.Document.Comments.Count);
        Assert.False(root.IsResolved);
        Assert.Null(root.ResolvedDate);

        var reply = session.Document.Comments.Values.Single(definition => definition.ParentId == rootId);
        Assert.Equal(root.ThreadId, reply.ThreadId);
    }

    [Fact]
    public async Task DeleteComment_RemovesMarkersAndDefinition()
    {
        var (session, router) = CreateReviewSession("Hello world");
        session.SetSelection(new TextRange(new TextPosition(0, 0), new TextPosition(0, 5)));
        await router.ExecuteAsync(EditorReviewCommandIds.Comments.NewComment);

        var commentId = session.Document.Comments.Keys.Single();
        Assert.True(await router.ExecuteAsync(EditorReviewCommandIds.Comments.DeleteComment, commentId));
        Assert.Empty(session.Document.Comments);

        var paragraph = session.Document.GetParagraph(0);
        Assert.DoesNotContain(paragraph.Inlines, inline => inline is CommentRangeStartInline or CommentRangeEndInline or CommentReferenceInline);
    }

    private static (EditorController Session, EditorCommandRouterAdapter Router) CreateReviewSession(string text)
    {
        var document = new Document();
        var session = new EditorController(new EditorTestTextMeasurer(), document);
        if (!string.IsNullOrEmpty(text))
        {
            session.InsertText(text);
        }

        var services = new EditorServices();
        var dispatcher = new EditorCommandDispatcher();
        var router = new EditorCommandRouterAdapter(dispatcher, session);
        var review = new EditorReviewCommandMap(router, session, services);
        review.Register();
        return (session, router);
    }
}

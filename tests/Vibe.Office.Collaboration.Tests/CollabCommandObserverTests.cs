using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Editor;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Word.Editor;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class CollabCommandObserverTests
{
    [Fact]
    public async Task InsertText_EmitsCollabOpsAndRecordsUndo()
    {
        var document = new Document();
        var session = new EditorController(new StubTextMeasurer(), document);
        var dispatcher = new EditorCommandDispatcher();
        var collabSession = new RecordingCollabSession();
        var history = new CollabOpHistory();
        var actorId = Guid.NewGuid();
        var batchFactory = new CollabBatchFactory(actorId, () => history.Version);
        var applier = new EditorCollabApplier(session);
        var undoRedo = new CollabUndoRedoService(
            collabSession,
            batchFactory,
            actorId,
            history.Transform,
            ops => applier.ApplyRemoteOps(ops),
            batch => history.AppendLocal(batch));
        var observer = new CollabCommandObserver(session, collabSession, undoRedo, batchFactory, batch => history.AppendLocal(batch));

        dispatcher.ExecutionObserver = observer;
        dispatcher.Dispatch(new InsertTextCommand("Hi"), session);

        Assert.Single(collabSession.Submitted);
        Assert.True(undoRedo.CanUndo);
        Assert.Equal("Hi", document.GetParagraph(0).Text);

        await undoRedo.UndoAsync();

        Assert.Equal(2, collabSession.Submitted.Count);
        Assert.Equal(string.Empty, document.GetParagraph(0).Text);
    }

    [Fact]
    public async Task UnsupportedCommand_UsesBlockOps()
    {
        var document = new Document();
        var session = new EditorController(new StubTextMeasurer(), document);
        var dispatcher = new EditorCommandDispatcher();
        var collabSession = new RecordingCollabSession();
        var history = new CollabOpHistory();
        var actorId = Guid.NewGuid();
        var batchFactory = new CollabBatchFactory(actorId, () => history.Version);
        var applier = new EditorCollabApplier(session);
        var undoRedo = new CollabUndoRedoService(
            collabSession,
            batchFactory,
            actorId,
            history.Transform,
            ops => applier.ApplyRemoteOps(ops),
            batch => history.AppendLocal(batch));

        var observer = new CollabCommandObserver(
            session,
            collabSession,
            undoRedo,
            batchFactory,
            batch => history.AppendLocal(batch));

        dispatcher.ExecutionObserver = observer;
        dispatcher.Dispatch(new InsertParagraphBreakCommand(), session);

        Assert.Single(collabSession.Submitted);
        Assert.True(undoRedo.CanUndo);

        await undoRedo.UndoAsync();
        Assert.Equal(2, collabSession.Submitted.Count);
    }

    private sealed class RecordingCollabSession : ICollabSession
    {
        public List<CollabOpBatch> Submitted { get; } = new();

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask SubmitLocalAsync(CollabOpBatch batch, CancellationToken cancellationToken = default)
        {
            Submitted.Add(batch);
            return ValueTask.CompletedTask;
        }
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

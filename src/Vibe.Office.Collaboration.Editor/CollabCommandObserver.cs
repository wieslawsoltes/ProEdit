using Vibe.Office.Collaboration;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Office.Collaboration.Editor;

public sealed class CollabCommandObserver : IEditorCommandExecutionObserver
{
    private readonly IEditorMutableSession _session;
    private readonly ICollabSession _collabSession;
    private readonly CollabUndoRedoService _undoRedo;
    private readonly ICollabBatchFactory _batchFactory;
    private readonly Action<CollabOpBatch>? _onLocalBatch;
    private readonly Action<int>? _onLocalApplied;
    private CommandSnapshot? _snapshot;
    private EditorSessionSnapshot? _snapshotBefore;
    private readonly Func<EditorSessionSnapshot, ValueTask>? _snapshotPublisher;
    private readonly CollabDocumentDiff _documentDiff = new();

    public CollabCommandObserver(
        IEditorMutableSession session,
        ICollabSession collabSession,
        CollabUndoRedoService undoRedo,
        ICollabBatchFactory batchFactory,
        Action<CollabOpBatch>? onLocalBatch = null,
        Action<int>? onLocalApplied = null,
        Func<EditorSessionSnapshot, ValueTask>? snapshotPublisher = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _collabSession = collabSession ?? throw new ArgumentNullException(nameof(collabSession));
        _undoRedo = undoRedo ?? throw new ArgumentNullException(nameof(undoRedo));
        _batchFactory = batchFactory ?? throw new ArgumentNullException(nameof(batchFactory));
        _onLocalBatch = onLocalBatch;
        _onLocalApplied = onLocalApplied;
        _snapshotPublisher = snapshotPublisher;
    }

    public void OnCommandExecuting(IEditorCommand command, IEditorMutableSession session)
    {
        _snapshot = CommandSnapshot.Capture(_session, command);
        _snapshotBefore = null;

        if (_snapshot is null && command is IEditorUndoableCommand undoable && undoable.IsUndoable && !_undoRedo.IsReplaying)
        {
            _snapshotBefore = CaptureSnapshot(_session);
        }
    }

    public void OnCommandExecuted(IEditorCommand command, IEditorMutableSession session, bool recordHistory)
    {
        if (!recordHistory || _undoRedo.IsReplaying)
        {
            _snapshot = null;
            _snapshotBefore = null;
            return;
        }

        if (_snapshot is not null && _snapshot.SupportsCommand(command))
        {
            if (!TryBuildOps(command, _snapshot, out var forwardOps, out var inverseOps))
            {
                _snapshot = null;
                return;
            }

            if (forwardOps.Count == 0 || inverseOps.Count == 0)
            {
                _snapshot = null;
                return;
            }

            var forwardBatch = _batchFactory.Create(forwardOps);

            _undoRedo.Record(forwardOps, inverseOps, forwardBatch.BaseVersion);
            _ = _collabSession.SubmitLocalAsync(forwardBatch);
            _onLocalBatch?.Invoke(forwardBatch);
            _onLocalApplied?.Invoke(forwardOps.Count);
            _snapshot = null;
            return;
        }

        if (_snapshotBefore.HasValue)
        {
            var before = _snapshotBefore.Value;
            var after = CaptureSnapshot(_session);
            if (_documentDiff.TryBuildOps(before.Document, after.Document, out var forwardOps, out var inverseOps)
                && forwardOps.Count > 0
                && inverseOps.Count > 0)
            {
                var forwardBatch = _batchFactory.Create(forwardOps);
                _undoRedo.Record(forwardOps, inverseOps, forwardBatch.BaseVersion);
                _ = _collabSession.SubmitLocalAsync(forwardBatch);
                _onLocalBatch?.Invoke(forwardBatch);
                _onLocalApplied?.Invoke(forwardOps.Count);
            }
            else
            {
                _undoRedo.RecordSnapshot(before, after);
                if (_snapshotPublisher is not null)
                {
                    _ = _snapshotPublisher(after);
                }
            }

            _snapshotBefore = null;
        }
    }

    private bool TryBuildOps(
        IEditorCommand command,
        CommandSnapshot snapshot,
        out IReadOnlyList<ICollabOp> forwardOps,
        out IReadOnlyList<ICollabOp> inverseOps)
    {
        forwardOps = Array.Empty<ICollabOp>();
        inverseOps = Array.Empty<ICollabOp>();

        switch (command)
        {
            case InsertTextCommand insert:
                return BuildInsertOps(snapshot, insert.Text, out forwardOps, out inverseOps);
            case BackspaceCommand:
                return BuildDeleteOps(snapshot, deleteBeforeCaret: true, out forwardOps, out inverseOps);
            case DeleteForwardCommand:
                return BuildDeleteOps(snapshot, deleteBeforeCaret: false, out forwardOps, out inverseOps);
            default:
                return false;
        }
    }

    private static bool BuildInsertOps(
        CommandSnapshot snapshot,
        string text,
        out IReadOnlyList<ICollabOp> forwardOps,
        out IReadOnlyList<ICollabOp> inverseOps)
    {
        forwardOps = Array.Empty<ICollabOp>();
        inverseOps = Array.Empty<ICollabOp>();

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var startOffset = snapshot.SelectionStartOffset;
        var endOffset = snapshot.SelectionEndOffset;
        var nodeId = snapshot.ParagraphNodeId;

        if (nodeId == Guid.Empty)
        {
            return false;
        }

        var ops = new List<ICollabOp>();
        var inverse = new List<ICollabOp>();

        if (!snapshot.SelectionIsEmpty)
        {
            var deletedText = snapshot.GetSelectedText();
            if (deletedText is null)
            {
                return false;
            }

            ops.Add(new DeleteRangeOp(TextAnchor.Before(nodeId, startOffset), TextAnchor.Before(nodeId, endOffset)));
            ops.Add(new InsertTextOp(TextAnchor.Before(nodeId, startOffset), text));

            inverse.Add(new DeleteRangeOp(TextAnchor.Before(nodeId, startOffset), TextAnchor.Before(nodeId, startOffset + text.Length)));
            if (deletedText.Length > 0)
            {
                inverse.Add(new InsertTextOp(TextAnchor.Before(nodeId, startOffset), deletedText));
            }
        }
        else
        {
            ops.Add(new InsertTextOp(TextAnchor.Before(nodeId, startOffset), text));
            inverse.Add(new DeleteRangeOp(TextAnchor.Before(nodeId, startOffset), TextAnchor.Before(nodeId, startOffset + text.Length)));
        }

        forwardOps = ops;
        inverseOps = inverse;
        return true;
    }

    private static bool BuildDeleteOps(
        CommandSnapshot snapshot,
        bool deleteBeforeCaret,
        out IReadOnlyList<ICollabOp> forwardOps,
        out IReadOnlyList<ICollabOp> inverseOps)
    {
        forwardOps = Array.Empty<ICollabOp>();
        inverseOps = Array.Empty<ICollabOp>();

        var nodeId = snapshot.ParagraphNodeId;
        if (nodeId == Guid.Empty)
        {
            return false;
        }

        var startOffset = snapshot.SelectionStartOffset;
        var endOffset = snapshot.SelectionEndOffset;

        if (snapshot.SelectionIsEmpty)
        {
            if (deleteBeforeCaret)
            {
                if (startOffset <= 0)
                {
                    return false;
                }

                startOffset -= 1;
                endOffset = startOffset + 1;
            }
            else
            {
                if (startOffset >= snapshot.ParagraphTextLength)
                {
                    return false;
                }

                endOffset = startOffset + 1;
            }
        }

        var deletedText = snapshot.GetTextSlice(startOffset, endOffset);
        if (deletedText is null)
        {
            return false;
        }

        forwardOps = new ICollabOp[]
        {
            new DeleteRangeOp(TextAnchor.Before(nodeId, startOffset), TextAnchor.Before(nodeId, endOffset))
        };

        inverseOps = deletedText.Length == 0
            ? Array.Empty<ICollabOp>()
            : new ICollabOp[]
            {
                new InsertTextOp(TextAnchor.Before(nodeId, startOffset), deletedText)
            };

        return true;
    }

    private sealed class CommandSnapshot
    {
        private readonly string _paragraphText;

        private CommandSnapshot(
            Guid paragraphNodeId,
            int selectionStartOffset,
            int selectionEndOffset,
            bool selectionIsEmpty,
            int paragraphTextLength,
            string paragraphText)
        {
            ParagraphNodeId = paragraphNodeId;
            SelectionStartOffset = selectionStartOffset;
            SelectionEndOffset = selectionEndOffset;
            SelectionIsEmpty = selectionIsEmpty;
            ParagraphTextLength = paragraphTextLength;
            _paragraphText = paragraphText;
        }

        public Guid ParagraphNodeId { get; }
        public int SelectionStartOffset { get; }
        public int SelectionEndOffset { get; }
        public bool SelectionIsEmpty { get; }
        public int ParagraphTextLength { get; }

        public static CommandSnapshot? Capture(IEditorMutableSession session, IEditorCommand command)
        {
            if (command is not InsertTextCommand
                && command is not BackspaceCommand
                && command is not DeleteForwardCommand)
            {
                return null;
            }

            var selection = session.Selection.Normalize();
            var paragraphIndex = selection.Start.ParagraphIndex;
            var endParagraphIndex = selection.End.ParagraphIndex;
            if (paragraphIndex != endParagraphIndex)
            {
                return null;
            }

            var paragraph = session.Document.GetParagraph(paragraphIndex);
            var text = DocumentEditHelpers.GetParagraphText(paragraph);
            var length = text.Length;

            var startOffset = Math.Clamp(selection.Start.Offset, 0, length);
            var endOffset = Math.Clamp(selection.End.Offset, 0, length);
            if (startOffset > endOffset)
            {
                (startOffset, endOffset) = (endOffset, startOffset);
            }

            return new CommandSnapshot(
                paragraph.NodeId,
                startOffset,
                endOffset,
                selection.IsEmpty,
                length,
                text);
        }

        public bool SupportsCommand(IEditorCommand command)
        {
            return command is InsertTextCommand or BackspaceCommand or DeleteForwardCommand;
        }

        public string? GetSelectedText()
        {
            return GetTextSlice(SelectionStartOffset, SelectionEndOffset);
        }

        public string? GetTextSlice(int start, int end)
        {
            if (end < start)
            {
                return null;
            }

            if (start < 0 || end > _paragraphText.Length)
            {
                return null;
            }

            return _paragraphText.Substring(start, end - start);
        }
    }

    private static EditorSessionSnapshot CaptureSnapshot(IEditorMutableSession session)
    {
        var document = DocumentClone.Clone(session.Document);
        return new EditorSessionSnapshot(document, session.Selection, session.Caret);
    }
}

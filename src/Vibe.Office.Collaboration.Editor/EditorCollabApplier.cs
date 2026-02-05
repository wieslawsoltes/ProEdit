using Vibe.Office.Collaboration;
using Vibe.Office.Collaboration.Persistence;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Office.Collaboration.Editor;

public sealed class EditorCollabApplier
{
    private readonly IEditorMutableSession _session;
    private readonly DocumentAnchorResolver _resolver = new();
    private readonly CollabBlockOpApplier _blockApplier = new();
    private readonly CollabPropertyOpApplier _propertyApplier = new();
    private readonly CollabDocumentResourceApplier _resourceApplier = new();
    private readonly CollabDocumentResourceSerializer _resourceSerializer = new();

    public EditorCollabApplier(IEditorMutableSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public void ApplyRemoteOps(IReadOnlyList<ICollabOp> ops, string? author = null)
    {
        ApplyRemoteOpsBatch(ops, author, refreshLayout: true);
    }

    public bool ApplyRemoteOpsBatch(IReadOnlyList<ICollabOp> ops, string? author, bool refreshLayout)
    {
        if (ops.Count == 0)
        {
            return false;
        }

        IDisposable? batchScope = null;
        if (_session is IEditorBatchEdit batchEdit)
        {
            batchScope = batchEdit.BeginBatchEdit();
        }

        var document = _session.Document;
        var previousAuthor = document.RevisionAuthorOverride;
        if (!string.IsNullOrWhiteSpace(author))
        {
            document.RevisionAuthorOverride = author;
        }

        var requiresRefresh = false;

        try
        {
            foreach (var op in ops)
            {
                if (ApplyOp(op))
                {
                    requiresRefresh = true;
                }
            }
        }
        finally
        {
            batchScope?.Dispose();
            document.RevisionAuthorOverride = previousAuthor;
        }

        if (requiresRefresh && refreshLayout)
        {
            _session.RefreshLayout();
        }

        return requiresRefresh;
    }

    private bool ApplyOp(ICollabOp op)
    {
        switch (op)
        {
            case InsertTextOp insert:
                ApplyInsertText(insert);
                return false;
            case DeleteRangeOp delete:
                ApplyDeleteRange(delete);
                return false;
            case InsertBlockOp:
            case DeleteBlockOp:
            case ReplaceBlockOp:
                return _blockApplier.Apply(_session.Document, op);
            case SetParagraphPropertiesOp setParagraph:
                return _propertyApplier.ApplyParagraph(_session.Document, setParagraph);
            case SetInlinePropertiesOp setInline:
                return _propertyApplier.ApplyInline(_session.Document, setInline);
            case ReplaceDocumentResourcesOp replaceResources:
                ApplyResources(replaceResources);
                return true;
            default:
                return false;
        }
    }

    private void ApplyInsertText(InsertTextOp insert)
    {
        if (!TryResolvePosition(insert.Anchor, out var position))
        {
            return;
        }

        _session.SetSelection(new TextRange(position, position));
        _session.InsertText(insert.Text.AsSpan());
    }

    private void ApplyDeleteRange(DeleteRangeOp delete)
    {
        if (delete.Start.NodeId != delete.End.NodeId)
        {
            return;
        }

        if (!TryResolvePosition(delete.Start, out var start)
            || !TryResolvePosition(delete.End, out var end))
        {
            return;
        }

        var range = new TextRange(start, end).Normalize();
        _session.SetSelection(range, SelectionUpdateMode.Replace);
        _session.DeleteForward();
    }

    private void ApplyResources(ReplaceDocumentResourcesOp op)
    {
        var resources = _resourceSerializer.Deserialize(op.Payload);
        _resourceApplier.Apply(_session.Document, resources);
    }

    private bool TryResolvePosition(TextAnchor anchor, out TextPosition position)
    {
        position = default;
        var document = _session.Document;
        if (!_resolver.TryResolveParagraph(document, anchor.NodeId, out var paragraph, out var paragraphIndex))
        {
            return false;
        }

        var length = DocumentEditHelpers.GetParagraphLength(paragraph);
        var offset = Math.Clamp(anchor.Offset, 0, length);
        position = new TextPosition(paragraphIndex, offset);
        return true;
    }
}

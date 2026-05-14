using ProEdit.Collaboration;
using ProEdit.Collaboration.Persistence;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Primitives;

namespace ProEdit.Collaboration.Editor;

public sealed class EditorCollabApplier
{
    private readonly IEditorMutableSession _session;
    private readonly DocumentAnchorResolver _resolver = new();
    private readonly CollabBlockOpApplier _blockApplier = new();
    private readonly CollabPropertyOpApplier _propertyApplier = new();
    private readonly CollabDocumentResourceApplier _resourceApplier = new();
    private readonly CollabDocumentResourceSerializer _resourceSerializer = new();
    private Guid _cachedParagraphNodeId;
    private ParagraphBlock? _cachedParagraph;
    private int _cachedParagraphIndex = -1;
    private bool _hasCachedParagraph;

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
        return ApplyRemoteOpsInternal(ops, author, refreshLayout, beginBatch: true);
    }

    public bool ApplyRemoteOpGroups(IReadOnlyList<(IReadOnlyList<ICollabOp> Ops, string? Author)> groups, bool refreshLayout)
    {
        if (groups.Count == 0)
        {
            return false;
        }

        IDisposable? batchScope = null;
        if (_session is IEditorBatchEdit batchEdit)
        {
            batchScope = batchEdit.BeginBatchEdit();
        }

        var requiresRefresh = false;
        try
        {
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group.Ops.Count == 0)
                {
                    continue;
                }

                if (ApplyRemoteOpsInternal(group.Ops, group.Author, refreshLayout: false, beginBatch: false))
                {
                    requiresRefresh = true;
                }
            }
        }
        finally
        {
            batchScope?.Dispose();
        }

        if (requiresRefresh && refreshLayout)
        {
            _session.RefreshLayout();
        }

        return requiresRefresh;
    }

    private bool ApplyRemoteOpsInternal(
        IReadOnlyList<ICollabOp> ops,
        string? author,
        bool refreshLayout,
        bool beginBatch)
    {
        if (ops.Count == 0)
        {
            return false;
        }

        IDisposable? batchScope = null;
        if (beginBatch && _session is IEditorBatchEdit batchEdit)
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
            {
                var applied = _blockApplier.Apply(_session.Document, op);
                if (applied)
                {
                    ResetAnchorCache();
                }

                return applied;
            }
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

        if (_session is IEditorDirectTextEdit directEdit)
        {
            directEdit.InsertTextAt(position, insert.Text.AsSpan());
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
        if (_session is IEditorDirectTextEdit directEdit)
        {
            directEdit.DeleteRange(range);
            return;
        }

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
        if (!TryResolveParagraph(document, anchor.NodeId, out var paragraph, out var paragraphIndex))
        {
            return false;
        }

        var length = DocumentEditHelpers.GetParagraphLength(paragraph);
        var offset = Math.Clamp(anchor.Offset, 0, length);
        position = new TextPosition(paragraphIndex, offset);
        return true;
    }

    private bool TryResolveParagraph(Document document, Guid paragraphNodeId, out ParagraphBlock paragraph, out int paragraphIndex)
    {
        if (_hasCachedParagraph && paragraphNodeId == _cachedParagraphNodeId && _cachedParagraph is not null)
        {
            paragraph = _cachedParagraph;
            paragraphIndex = _cachedParagraphIndex;
            return true;
        }

        if (!_resolver.TryResolveParagraph(document, paragraphNodeId, out paragraph, out paragraphIndex))
        {
            return false;
        }

        _cachedParagraphNodeId = paragraphNodeId;
        _cachedParagraph = paragraph;
        _cachedParagraphIndex = paragraphIndex;
        _hasCachedParagraph = true;
        return true;
    }

    private void ResetAnchorCache()
    {
        _cachedParagraphNodeId = Guid.Empty;
        _cachedParagraph = null;
        _cachedParagraphIndex = -1;
        _hasCachedParagraph = false;
    }
}

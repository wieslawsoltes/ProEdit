namespace Vibe.Office.Collaboration;

/// <summary>
/// Tracks applied collaboration operations and performs basic transforms for concurrent edits.
/// </summary>
public sealed class CollabOpHistory
{
    private readonly List<AppliedOp> _history = new();
    private readonly int _maxEntries;
    private readonly Queue<Guid> _recentBatchQueue = new();
    private readonly HashSet<Guid> _recentBatchIds = new();
    private readonly int _maxRecentBatches;

    public CollabOpHistory(int maxEntries = 50_000)
    {
        _maxEntries = maxEntries <= 0 ? 1 : maxEntries;
        _maxRecentBatches = Math.Max(1024, Math.Min(_maxEntries * 2, 200_000));
    }

    /// <summary>
    /// Gets the current logical version.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>
    /// Gets the minimum retained version available for transforms.
    /// </summary>
    public long MinRetainedVersion => _history.Count == 0 ? Version : _history[0].Version;

    /// <summary>
    /// Clears the history and resets to the supplied version.
    /// </summary>
    public void Reset(long version = 0)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        _history.Clear();
        _recentBatchQueue.Clear();
        _recentBatchIds.Clear();
        Version = version;
    }

    /// <summary>
    /// Appends a local batch to the history.
    /// </summary>
    public void AppendLocal(CollabOpBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        TrackBatch(batch.BatchId);
        AppendBatch(batch, batch.Ops);
    }

    /// <summary>
    /// Transforms a remote batch against the history and appends the transformed ops.
    /// </summary>
    public CollabTransformResult TransformRemote(CollabOpBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (IsDuplicateBatch(batch.BatchId))
        {
            return new CollabTransformResult(Array.Empty<ICollabOp>(), RequiresResync: false);
        }

        var result = Transform(batch);
        if (result.RequiresResync || result.Ops.Count == 0)
        {
            return result;
        }

        AppendBatch(batch, result.Ops);
        TrackBatch(batch.BatchId);
        return result;
    }

    /// <summary>
    /// Transforms a batch against the history without mutating the history.
    /// </summary>
    public CollabTransformResult Transform(CollabOpBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var minSupportedVersion = Math.Max(0, MinRetainedVersion - 1);
        if (batch.BaseVersion < minSupportedVersion)
        {
            return new CollabTransformResult(Array.Empty<ICollabOp>(), RequiresResync: true);
        }

        if (batch.Ops.Count == 0)
        {
            return new CollabTransformResult(Array.Empty<ICollabOp>(), RequiresResync: false);
        }

        var transformed = new List<ICollabOp>(batch.Ops.Count);
        var historyCount = _history.Count;
        var startIndex = GetHistoryStartIndex(batch.BaseVersion, historyCount);

        foreach (var op in batch.Ops)
        {
            var current = TransformOp(op, batch.ActorId, startIndex, historyCount);
            if (current is null)
            {
                continue;
            }

            transformed.Add(current);
        }

        return new CollabTransformResult(transformed, RequiresResync: false);
    }

    private ICollabOp? TransformOp(ICollabOp op, Guid incomingActorId, int startIndex, int historyCount)
    {
        var current = op;
        for (var i = startIndex; i < historyCount; i++)
        {
            var entry = _history[i];
            current = CollabOpTransformer.Transform(current, incomingActorId, entry);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private int GetHistoryStartIndex(long baseVersion, int historyCount)
    {
        if (historyCount == 0)
        {
            return 0;
        }

        var minVersion = _history[0].Version;
        if (baseVersion < minVersion)
        {
            return 0;
        }

        var offset = baseVersion - minVersion + 1;
        if (offset <= 0)
        {
            return 0;
        }

        if (offset >= historyCount)
        {
            return historyCount;
        }

        return (int)offset;
    }

    private void AppendBatch(CollabOpBatch batch, IReadOnlyList<ICollabOp> ops)
    {
        foreach (var op in ops)
        {
            Append(batch.ActorId, batch.Sequence, batch.Lamport, op);
        }
    }

    private void Append(Guid actorId, long sequence, long lamport, ICollabOp op)
    {
        Version++;
        _history.Add(new AppliedOp(Version, actorId, sequence, lamport, op));

        if (_history.Count <= _maxEntries)
        {
            return;
        }

        var overflow = _history.Count - _maxEntries;
        if (overflow > 0)
        {
            _history.RemoveRange(0, overflow);
        }
    }

    private bool IsDuplicateBatch(Guid batchId)
    {
        if (batchId == Guid.Empty)
        {
            return false;
        }

        return _recentBatchIds.Contains(batchId);
    }

    private void TrackBatch(Guid batchId)
    {
        if (batchId == Guid.Empty)
        {
            return;
        }

        if (_recentBatchIds.Add(batchId))
        {
            _recentBatchQueue.Enqueue(batchId);
        }

        while (_recentBatchQueue.Count > _maxRecentBatches)
        {
            var expired = _recentBatchQueue.Dequeue();
            _recentBatchIds.Remove(expired);
        }
    }

    /// <summary>
    /// Result of transforming a remote batch.
    /// </summary>
    public sealed record CollabTransformResult(IReadOnlyList<ICollabOp> Ops, bool RequiresResync);

    private readonly record struct AppliedOp(long Version, Guid ActorId, long Sequence, long Lamport, ICollabOp Op);

    private static class CollabOpTransformer
    {
        public static ICollabOp? Transform(ICollabOp incoming, Guid incomingActorId, AppliedOp existing)
        {
            return existing.Op switch
            {
                InsertTextOp insert => TransformAgainstInsert(incoming, incomingActorId, existing.ActorId, insert),
                DeleteRangeOp delete => TransformAgainstDelete(incoming, incomingActorId, delete),
                InsertBlockOp insertBlock => TransformAgainstInsertBlock(incoming, incomingActorId, existing.ActorId, insertBlock),
                DeleteBlockOp deleteBlock => TransformAgainstDeleteBlock(incoming, deleteBlock),
                _ => incoming
            };
        }

        private static ICollabOp? TransformAgainstInsert(
            ICollabOp incoming,
            Guid incomingActorId,
            Guid existingActorId,
            InsertTextOp insert)
        {
            return incoming switch
            {
                InsertTextOp incomingInsert => TransformInsertAgainstInsert(incomingInsert, incomingActorId, existingActorId, insert),
                DeleteRangeOp incomingDelete => TransformDeleteAgainstInsert(incomingDelete, incomingActorId, existingActorId, insert),
                _ => incoming
            };
        }

        private static ICollabOp? TransformAgainstDelete(ICollabOp incoming, Guid incomingActorId, DeleteRangeOp delete)
        {
            return incoming switch
            {
                InsertTextOp incomingInsert => TransformInsertAgainstDelete(incomingInsert, delete),
                DeleteRangeOp incomingDelete => TransformDeleteAgainstDelete(incomingDelete, delete),
                _ => incoming
            };
        }

        private static ICollabOp? TransformAgainstInsertBlock(
            ICollabOp incoming,
            Guid incomingActorId,
            Guid existingActorId,
            InsertBlockOp insert)
        {
            return incoming switch
            {
                InsertBlockOp incomingInsert => TransformInsertBlockAgainstInsert(incomingInsert, incomingActorId, existingActorId, insert),
                DeleteBlockOp incomingDelete => TransformDeleteBlockAgainstInsert(incomingDelete, insert),
                _ => incoming
            };
        }

        private static ICollabOp? TransformAgainstDeleteBlock(ICollabOp incoming, DeleteBlockOp delete)
        {
            return incoming switch
            {
                InsertBlockOp incomingInsert => TransformInsertBlockAgainstDelete(incomingInsert, delete),
                DeleteBlockOp incomingDelete => TransformDeleteBlockAgainstDelete(incomingDelete, delete),
                ReplaceBlockOp incomingReplace => incomingReplace.BlockNodeId == delete.BlockNodeId ? null : incomingReplace,
                InsertTextOp incomingInsertText => incomingInsertText.Anchor.NodeId == delete.BlockNodeId ? null : incomingInsertText,
                DeleteRangeOp incomingDeleteRange => ShouldDropDeleteRange(incomingDeleteRange, delete.BlockNodeId) ? null : incomingDeleteRange,
                SetParagraphPropertiesOp incomingParagraphProps => incomingParagraphProps.ParagraphNodeId == delete.BlockNodeId ? null : incomingParagraphProps,
                _ => incoming
            };
        }

        private static InsertTextOp TransformInsertAgainstInsert(
            InsertTextOp incoming,
            Guid incomingActorId,
            Guid existingActorId,
            InsertTextOp existing)
        {
            if (incoming.Anchor.NodeId != existing.Anchor.NodeId)
            {
                return incoming;
            }

            var offset = incoming.Anchor.Offset;
            if (offset > existing.Anchor.Offset
                || (offset == existing.Anchor.Offset
                    && ShouldShiftForInsert(incomingActorId, incoming.Anchor.Bias, existingActorId, existing.Anchor.Bias)))
            {
                offset += existing.Text.Length;
            }

            return incoming with { Anchor = WithOffset(incoming.Anchor, offset) };
        }

        private static DeleteRangeOp TransformDeleteAgainstInsert(
            DeleteRangeOp incoming,
            Guid incomingActorId,
            Guid existingActorId,
            InsertTextOp existing)
        {
            if (incoming.Start.NodeId != existing.Anchor.NodeId
                || incoming.End.NodeId != existing.Anchor.NodeId)
            {
                return incoming;
            }

            var start = incoming.Start;
            var end = incoming.End;
            NormalizeRange(ref start, ref end);

            var insertOffset = existing.Anchor.Offset;
            var insertLength = existing.Text.Length;

            if (insertOffset < start.Offset
                || (insertOffset == start.Offset
                    && ShouldShiftForInsert(incomingActorId, start.Bias, existingActorId, existing.Anchor.Bias)))
            {
                start = WithOffset(start, start.Offset + insertLength);
                end = WithOffset(end, end.Offset + insertLength);
                return new DeleteRangeOp(start, end);
            }

            if (insertOffset > end.Offset)
            {
                return new DeleteRangeOp(start, end);
            }

            end = WithOffset(end, end.Offset + insertLength);
            return new DeleteRangeOp(start, end);
        }

        private static InsertTextOp TransformInsertAgainstDelete(InsertTextOp incoming, DeleteRangeOp existing)
        {
            if (incoming.Anchor.NodeId != existing.Start.NodeId
                || incoming.Anchor.NodeId != existing.End.NodeId)
            {
                return incoming;
            }

            var start = existing.Start;
            var end = existing.End;
            NormalizeRange(ref start, ref end);

            var offset = TransformOffsetAgainstDelete(incoming.Anchor.Offset, start.Offset, end.Offset);
            return incoming with { Anchor = WithOffset(incoming.Anchor, offset) };
        }

        private static ICollabOp? TransformDeleteAgainstDelete(DeleteRangeOp incoming, DeleteRangeOp existing)
        {
            if (incoming.Start.NodeId != existing.Start.NodeId
                || incoming.End.NodeId != existing.End.NodeId)
            {
                return incoming;
            }

            var start = incoming.Start;
            var end = incoming.End;
            NormalizeRange(ref start, ref end);

            var existingStart = existing.Start;
            var existingEnd = existing.End;
            NormalizeRange(ref existingStart, ref existingEnd);

            var newStartOffset = TransformOffsetAgainstDelete(start.Offset, existingStart.Offset, existingEnd.Offset);
            var newEndOffset = TransformOffsetAgainstDelete(end.Offset, existingStart.Offset, existingEnd.Offset);

            if (newEndOffset < newStartOffset)
            {
                (newStartOffset, newEndOffset) = (newEndOffset, newStartOffset);
            }

            if (newEndOffset == newStartOffset)
            {
                return null;
            }

            start = WithOffset(start, newStartOffset);
            end = WithOffset(end, newEndOffset);
            NormalizeRange(ref start, ref end);
            return new DeleteRangeOp(start, end);
        }

        private static InsertBlockOp TransformInsertBlockAgainstInsert(
            InsertBlockOp incoming,
            Guid incomingActorId,
            Guid existingActorId,
            InsertBlockOp existing)
        {
            if (incoming.ParentNodeId != existing.ParentNodeId)
            {
                return incoming;
            }

            if (!CollabPositionToken.TryGetIndex(incoming.Position, out var incomingIndex)
                || !CollabPositionToken.TryGetIndex(existing.Position, out var existingIndex))
            {
                return incoming;
            }

            if (incomingIndex > existingIndex
                || (incomingIndex == existingIndex
                    && ShouldShiftForInsert(incomingActorId, AnchorBias.Before, existingActorId, AnchorBias.Before)))
            {
                incomingIndex += 1;
            }

            return incoming with { Position = CollabPositionToken.FromIndex(incomingIndex) };
        }

        private static DeleteBlockOp TransformDeleteBlockAgainstInsert(DeleteBlockOp incoming, InsertBlockOp existing)
        {
            if (incoming.ParentNodeId != existing.ParentNodeId)
            {
                return incoming;
            }

            if (!CollabPositionToken.TryGetIndex(incoming.Position, out var incomingIndex)
                || !CollabPositionToken.TryGetIndex(existing.Position, out var existingIndex))
            {
                return incoming;
            }

            if (incomingIndex >= existingIndex)
            {
                incomingIndex += 1;
            }

            return incoming with { Position = CollabPositionToken.FromIndex(incomingIndex) };
        }

        private static InsertBlockOp TransformInsertBlockAgainstDelete(InsertBlockOp incoming, DeleteBlockOp existing)
        {
            if (incoming.ParentNodeId != existing.ParentNodeId)
            {
                return incoming;
            }

            if (!CollabPositionToken.TryGetIndex(incoming.Position, out var incomingIndex)
                || !CollabPositionToken.TryGetIndex(existing.Position, out var existingIndex))
            {
                return incoming;
            }

            if (incomingIndex > existingIndex)
            {
                incomingIndex = Math.Max(0, incomingIndex - 1);
            }

            return incoming with { Position = CollabPositionToken.FromIndex(incomingIndex) };
        }

        private static DeleteBlockOp? TransformDeleteBlockAgainstDelete(DeleteBlockOp incoming, DeleteBlockOp existing)
        {
            if (incoming.ParentNodeId != existing.ParentNodeId)
            {
                return incoming;
            }

            if (!CollabPositionToken.TryGetIndex(incoming.Position, out var incomingIndex)
                || !CollabPositionToken.TryGetIndex(existing.Position, out var existingIndex))
            {
                return incoming;
            }

            if (incomingIndex > existingIndex)
            {
                incomingIndex = Math.Max(0, incomingIndex - 1);
            }
            else if (incomingIndex == existingIndex && incoming.BlockNodeId == existing.BlockNodeId)
            {
                return null;
            }

            return incoming with { Position = CollabPositionToken.FromIndex(incomingIndex) };
        }

        private static int TransformOffsetAgainstDelete(int offset, int deleteStart, int deleteEnd)
        {
            var length = Math.Max(0, deleteEnd - deleteStart);
            if (offset <= deleteStart)
            {
                return offset;
            }

            if (offset >= deleteEnd)
            {
                return offset - length;
            }

            return deleteStart;
        }

        private static bool ShouldShiftForInsert(Guid incomingActorId, AnchorBias incomingBias, Guid existingActorId, AnchorBias existingBias)
        {
            var biasCompare = incomingBias.CompareTo(existingBias);
            if (biasCompare != 0)
            {
                return biasCompare > 0;
            }

            return incomingActorId.CompareTo(existingActorId) > 0;
        }

        private static TextAnchor WithOffset(TextAnchor anchor, int offset)
        {
            return new TextAnchor(anchor.NodeId, Math.Max(0, offset), anchor.Bias);
        }

        private static void NormalizeRange(ref TextAnchor start, ref TextAnchor end)
        {
            if (end.Offset < start.Offset)
            {
                (start, end) = (end, start);
            }
        }

        private static bool ShouldDropDeleteRange(DeleteRangeOp deleteRange, Guid deletedBlockId)
        {
            return deleteRange.Start.NodeId == deletedBlockId && deleteRange.End.NodeId == deletedBlockId;
        }
    }
}

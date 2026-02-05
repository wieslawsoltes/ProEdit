using System.Linq;
using Vibe.Office.Collaboration.Persistence;
using Vibe.Office.Documents;

namespace Vibe.Office.Collaboration;

public sealed class CollabDocumentDiff
{
    private readonly CollabBlockSerializer _serializer;
    private readonly CollabDocumentResourceSerializer _resourceSerializer;

    public CollabDocumentDiff(
        CollabBlockSerializer? serializer = null,
        CollabDocumentResourceSerializer? resourceSerializer = null)
    {
        _serializer = serializer ?? new CollabBlockSerializer();
        _resourceSerializer = resourceSerializer ?? new CollabDocumentResourceSerializer();
    }

    public bool TryBuildOps(
        Document before,
        Document after,
        out IReadOnlyList<ICollabOp> forwardOps,
        out IReadOnlyList<ICollabOp> inverseOps)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var forward = new List<ICollabOp>();
        var inverse = new List<ICollabOp>();

        var beforeResources = _resourceSerializer.Serialize(before);
        var afterResources = _resourceSerializer.Serialize(after);
        if (!beforeResources.AsSpan().SequenceEqual(afterResources))
        {
            forward.Add(new ReplaceDocumentResourcesOp(afterResources));
            inverse.Add(new ReplaceDocumentResourcesOp(beforeResources));
        }

        var beforeContainers = CollabContainerCatalog.Enumerate(before);
        var afterContainers = CollabContainerCatalog.Enumerate(after);

        var containerOrder = new List<Guid>();
        var seen = new HashSet<Guid>();
        AppendContainers(beforeContainers, containerOrder, seen);
        AppendContainers(afterContainers, containerOrder, seen);

        var beforeMap = beforeContainers.ToDictionary(container => container.Id, container => container.Blocks);
        var afterMap = afterContainers.ToDictionary(container => container.Id, container => container.Blocks);

        foreach (var containerId in containerOrder)
        {
            beforeMap.TryGetValue(containerId, out var beforeBlocks);
            afterMap.TryGetValue(containerId, out var afterBlocks);
            DiffContainer(containerId, beforeBlocks ?? Array.Empty<Block>(), afterBlocks ?? Array.Empty<Block>(), before, after, forward, inverse);
        }

        forwardOps = forward;
        inverseOps = inverse;
        return forward.Count > 0 || inverse.Count > 0;
    }

    private void DiffContainer(
        Guid containerId,
        IList<Block> beforeBlocks,
        IList<Block> afterBlocks,
        Document beforeDocument,
        Document afterDocument,
        List<ICollabOp> forward,
        List<ICollabOp> inverse)
    {
        if (beforeBlocks.Count == 0 && afterBlocks.Count == 0)
        {
            return;
        }

        var beforeIds = new Guid[beforeBlocks.Count];
        for (var i = 0; i < beforeIds.Length; i++)
        {
            beforeIds[i] = beforeBlocks[i].NodeId;
        }

        var afterIds = new Guid[afterBlocks.Count];
        for (var i = 0; i < afterIds.Length; i++)
        {
            afterIds[i] = afterBlocks[i].NodeId;
        }

        ComputeLcs(beforeIds, afterIds, out var keepBefore, out var keepAfter);

        var iBefore = 0;
        var iAfter = 0;
        var currentIndex = 0;

        while (iBefore < beforeIds.Length || iAfter < afterIds.Length)
        {
            if (iBefore < beforeIds.Length
                && iAfter < afterIds.Length
                && keepBefore[iBefore]
                && keepAfter[iAfter]
                && beforeIds[iBefore] == afterIds[iAfter])
            {
                var beforeBlock = beforeBlocks[iBefore];
                var afterBlock = afterBlocks[iAfter];

                if (!BlocksEquivalent(beforeBlock, afterBlock, beforeDocument, afterDocument))
                {
                    var payloadAfter = _serializer.Serialize(afterBlock, afterDocument);
                    var payloadBefore = _serializer.Serialize(beforeBlock, beforeDocument);
                    forward.Add(new ReplaceBlockOp(beforeBlock.NodeId, payloadAfter));
                    inverse.Add(new ReplaceBlockOp(beforeBlock.NodeId, payloadBefore));
                }

                iBefore++;
                iAfter++;
                currentIndex++;
                continue;
            }

            if (iBefore < beforeIds.Length && (iAfter >= afterIds.Length || !keepBefore[iBefore]))
            {
                var removed = beforeBlocks[iBefore];
                forward.Add(new DeleteBlockOp(containerId, CollabPositionToken.FromIndex(currentIndex), removed.NodeId));
                var payload = _serializer.Serialize(removed, beforeDocument);
                inverse.Add(new InsertBlockOp(containerId, CollabPositionToken.FromIndex(currentIndex), removed.GetType().Name, payload));
                iBefore++;
                continue;
            }

            if (iAfter < afterIds.Length && (iBefore >= beforeIds.Length || !keepAfter[iAfter]))
            {
                var added = afterBlocks[iAfter];
                var payload = _serializer.Serialize(added, afterDocument);
                forward.Add(new InsertBlockOp(containerId, CollabPositionToken.FromIndex(currentIndex), added.GetType().Name, payload));
                inverse.Add(new DeleteBlockOp(containerId, CollabPositionToken.FromIndex(currentIndex), added.NodeId));
                iAfter++;
                currentIndex++;
                continue;
            }

            // Fallback advance to avoid infinite loops.
            if (iBefore < beforeIds.Length)
            {
                iBefore++;
                currentIndex++;
            }
            if (iAfter < afterIds.Length)
            {
                iAfter++;
            }
        }
    }

    private bool BlocksEquivalent(Block before, Block after, Document beforeDocument, Document afterDocument)
    {
        if (before.GetType() != after.GetType())
        {
            return false;
        }

        var beforePayload = _serializer.SerializeForDiff(before);
        var afterPayload = _serializer.SerializeForDiff(after);
        return beforePayload.AsSpan().SequenceEqual(afterPayload);
    }

    private static void AppendContainers(
        IReadOnlyList<CollabBlockContainer> containers,
        ICollection<Guid> ordered,
        ISet<Guid> seen)
    {
        foreach (var container in containers)
        {
            if (seen.Add(container.Id))
            {
                ordered.Add(container.Id);
            }
        }
    }

    private static void ComputeLcs(Guid[] before, Guid[] after, out bool[] keepBefore, out bool[] keepAfter)
    {
        keepBefore = new bool[before.Length];
        keepAfter = new bool[after.Length];

        if (before.Length == 0 || after.Length == 0)
        {
            return;
        }

        var lengths = new int[before.Length + 1, after.Length + 1];
        for (var i = before.Length - 1; i >= 0; i--)
        {
            for (var j = after.Length - 1; j >= 0; j--)
            {
                if (before[i] == after[j])
                {
                    lengths[i, j] = lengths[i + 1, j + 1] + 1;
                }
                else
                {
                    lengths[i, j] = Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
                }
            }
        }

        var a = 0;
        var b = 0;
        while (a < before.Length && b < after.Length)
        {
            if (before[a] == after[b])
            {
                keepBefore[a] = true;
                keepAfter[b] = true;
                a++;
                b++;
            }
            else if (lengths[a + 1, b] >= lengths[a, b + 1])
            {
                a++;
            }
            else
            {
                b++;
            }
        }
    }
}

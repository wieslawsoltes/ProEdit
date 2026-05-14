using ProEdit.Documents;

namespace ProEdit.Collaboration;

public sealed class CollabDocumentResourceApplier
{
    public void Apply(Document target, Document resources)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(resources);

        var preservedBlocks = new Dictionary<Guid, List<Block>>();
        foreach (var container in CollabContainerCatalog.Enumerate(target))
        {
            preservedBlocks[container.Id] = new List<Block>(container.Blocks);
        }

        DocumentClone.Copy(resources, target);

        foreach (var container in CollabContainerCatalog.Enumerate(target))
        {
            if (!preservedBlocks.TryGetValue(container.Id, out var blocks))
            {
                continue;
            }

            container.Blocks.Clear();
            foreach (var block in blocks)
            {
                container.Blocks.Add(block);
            }
        }
    }
}

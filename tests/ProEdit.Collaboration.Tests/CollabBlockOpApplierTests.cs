using ProEdit.Collaboration;
using ProEdit.Collaboration.Persistence;
using ProEdit.Documents;
using Xunit;

namespace ProEdit.Collaboration.Tests;

public sealed class CollabBlockOpApplierTests
{
    [Fact]
    public void InsertBlockDoesNotRemapListIdsWhenDefinitionsMatch()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.ListDefinitions.Clear();

        var listId = 1;
        document.ListDefinitions[listId] = ListDefinitionDefaults.CreateNumbered(listId, multilevel: false);

        var existing = new ParagraphBlock("one", new ListInfo(ListKind.Numbered, level: 0, listId: listId));
        document.Blocks.Add(existing);

        var serializer = new CollabBlockSerializer();
        var payload = serializer.Serialize(existing, document);

        var op = new InsertBlockOp(CollabContainerIds.Body, CollabPositionToken.FromIndex(1), nameof(ParagraphBlock), payload);
        var applier = new CollabBlockOpApplier();

        var applied = applier.Apply(document, op);

        Assert.True(applied);
        Assert.True(document.ListDefinitions.ContainsKey(listId));

        var inserted = document.Blocks[1] as ParagraphBlock;
        Assert.NotNull(inserted);
        Assert.Equal(listId, inserted!.ListInfo?.ListId);
    }
}

using Vibe.Office.Collaboration;
using Vibe.Office.Documents;
using Xunit;

namespace Vibe.Office.Collaboration.Tests;

public sealed class AnchorResolverTests
{
    [Fact]
    public void TryResolveParagraph_FindsParagraphByNodeId()
    {
        var document = new Document();
        var second = new ParagraphBlock("Second");
        document.Blocks.Add(second);

        var resolver = new DocumentAnchorResolver();

        var result = resolver.TryResolveParagraph(document, second.NodeId, out var resolved, out var index);

        Assert.True(result);
        Assert.Equal(second.NodeId, resolved.NodeId);
        Assert.Equal(1, index);
    }
}

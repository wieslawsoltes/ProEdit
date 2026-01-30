using Vibe.Office.Layout;
using Xunit;

namespace Vibe.Office.Layout.Tests;

public sealed class HyphenationTests
{
    [Fact]
    public void HyphenationTrie_ResolvesPatternPoints()
    {
        var trie = HyphenationPatternTrie.Build(new[] { "foo1bar" });

        var points = trie.GetHyphenationPoints("foobar", 1, 1);

        Assert.Equal(new[] { 3 }, points);
    }
}

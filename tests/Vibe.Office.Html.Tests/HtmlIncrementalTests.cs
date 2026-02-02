using Vibe.Office.Html.Ast;
using Xunit;

namespace Vibe.Office.Html.Tests;

public class HtmlIncrementalTests
{
    [Fact]
    public void IncrementalParser_ReusesIds_AfterEdit()
    {
        var parser = new HtmlIncrementalParser();
        var oldText = "<p>One</p><p>Two</p>";
        var newText = "<p>One!</p><p>Two</p>";

        var previous = parser.Parse(oldText);
        var current = parser.Update(previous, oldText, newText, out _);

        var oldParagraphs = FindElements(previous, "p");
        var newParagraphs = FindElements(current, "p");

        Assert.True(oldParagraphs.Count >= 2);
        Assert.True(newParagraphs.Count >= 2);
        Assert.Equal(oldParagraphs[1].Id, newParagraphs[1].Id);
    }

    [Fact]
    public void IncrementalParser_ReusesIds_BeforeEdit()
    {
        var parser = new HtmlIncrementalParser();
        var oldText = "<p>One</p><p>Two</p>";
        var newText = "<p>One</p><p>Two!</p>";

        var previous = parser.Parse(oldText);
        var current = parser.Update(previous, oldText, newText, out _);

        var oldParagraphs = FindElements(previous, "p");
        var newParagraphs = FindElements(current, "p");

        Assert.True(oldParagraphs.Count >= 2);
        Assert.True(newParagraphs.Count >= 2);
        Assert.Equal(oldParagraphs[0].Id, newParagraphs[0].Id);
    }

    [Fact]
    public void IncrementalSerializer_UsesMinimalPatchRange()
    {
        var parser = new HtmlIncrementalParser();
        var serializer = new HtmlIncrementalSerializer();
        var oldText = "<p>A</p><p>B</p><p>C</p>";
        var newText = "<p>A</p><p>BB</p><p>C</p>";

        var previous = parser.Parse(oldText);
        var current = parser.Update(previous, oldText, newText, out _);
        var update = serializer.SerializeIncremental(current, previous, oldText);

        Assert.Single(update.Edits);
        var edit = update.Edits[0];
        Assert.True(edit.Start > 0);
        Assert.True(edit.Length < oldText.Length);
        Assert.Contains("BB", update.Text, StringComparison.Ordinal);
    }

    private static List<HtmlElementNode> FindElements(HtmlDocument document, string name)
    {
        var results = new List<HtmlElementNode>();
        Traverse(document, node =>
        {
            if (node is HtmlElementNode element
                && string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(element);
            }
        });
        return results;
    }

    private static void Traverse(HtmlNode node, Action<HtmlNode> visitor)
    {
        visitor(node);
        switch (node)
        {
            case HtmlDocument doc:
                foreach (var child in doc.Children)
                {
                    Traverse(child, visitor);
                }
                break;
            case HtmlElementNode element:
                foreach (var child in element.Children)
                {
                    Traverse(child, visitor);
                }
                break;
        }
    }
}

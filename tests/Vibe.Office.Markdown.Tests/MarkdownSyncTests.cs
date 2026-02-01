using Vibe.Office.Documents;
using Vibe.Office.Markdown.Ast;
using Xunit;

namespace Vibe.Office.Markdown.Tests;

public class MarkdownSyncTests
{
    [Fact]
    public void RawMarkdownEdit_UpdatesDocument()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var parser = new MarkdownIncrementalParser(options);

        var oldText = "Hello";
        var previous = parser.Parse(oldText);
        var updated = parser.Update(previous, oldText, "Hello world", out var edits);

        Assert.Single(edits);
        var document = MarkdownAstConverter.ToDocument(updated, options);
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        Assert.Equal("Hello world", MarkdownTestHelpers.GetParagraphText(paragraph));
    }

    [Fact]
    public void DocumentEdit_UpdatesMarkdown()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var document = MarkdownDocumentConverter.FromMarkdown("Hello", options);
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);

        if (paragraph.Inlines.Count > 0)
        {
            paragraph.Inlines[0] = new RunInline("Updated");
        }
        else
        {
            paragraph.Text = "Updated";
        }

        var markdown = MarkdownDocumentConverter.ToMarkdown(document, options);
        Assert.Contains("Updated", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalParser_PreservesUnchangedNodeIds()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var parser = new MarkdownIncrementalParser(options);

        var oldText = "First\n\nSecond";
        var previous = parser.Parse(oldText);
        var updated = parser.Update(previous, oldText, "First\n\nSecond updated", out _);

        Assert.Equal(previous.Blocks[0].Id, updated.Blocks[0].Id);
        Assert.NotEqual(previous.Blocks[1].Id, updated.Blocks[1].Id);
    }

    [Fact]
    public void IncrementalSerializer_ProducesPatchThatMatchesFullSerialization()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var parser = new MarkdownParser(options);
        var serializer = new MarkdownSerializer(options);
        var incremental = new MarkdownIncrementalSerializer(options);

        var oldText = "First\n\nSecond";
        var previous = parser.Parse(oldText);
        var updated = parser.Parse("First\n\nSecond updated");

        var update = incremental.SerializeIncremental(updated, previous, oldText);
        var patched = MarkdownTextPatch.Apply(oldText, update.Edits);
        var full = serializer.Serialize(updated);

        Assert.Equal(full, update.Text);
        Assert.Equal(full, patched);
        Assert.Single(update.Edits);
    }
}

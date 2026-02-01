using Vibe.Office.Documents;
using Xunit;

namespace Vibe.Office.Markdown.Tests;

public class MarkdownDualViewConflictTests
{
    [Fact]
    public void ConflictResolution_LastWriterMarkdown_IsDeterministic()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var sync = new MarkdownDualViewSync(options);
        var baseState = sync.Initialize("Hello\n\nWorld");

        var markdownEdit = "Hello\n\nWorld!!";
        void DocumentEdit(Document doc)
        {
            var paragraph = Assert.IsType<ParagraphBlock>(doc.Blocks[0]);
            paragraph.Text = "Hi";
            paragraph.Inlines.Clear();
        }

        var resolved = sync.ResolveConflict(baseState, markdownEdit, DocumentEdit, MarkdownSyncSource.Markdown);
        var expected = MarkdownDocumentConverter.ToMarkdown(
            MarkdownDocumentConverter.FromMarkdown(markdownEdit, options),
            options);

        Assert.Equal(expected, resolved.MarkdownText);
    }

    [Fact]
    public void ConflictResolution_LastWriterDocument_IsDeterministic()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var sync = new MarkdownDualViewSync(options);
        var baseState = sync.Initialize("Hello\n\nWorld");

        var markdownEdit = "Hello\n\nWorld!!";
        void DocumentEdit(Document doc)
        {
            var paragraph = Assert.IsType<ParagraphBlock>(doc.Blocks[0]);
            paragraph.Text = "Hi";
            paragraph.Inlines.Clear();
        }

        var resolved = sync.ResolveConflict(baseState, markdownEdit, DocumentEdit, MarkdownSyncSource.Document);
        Assert.Contains("Hi", resolved.MarkdownText, StringComparison.Ordinal);
        Assert.DoesNotContain("World!!", resolved.MarkdownText, StringComparison.Ordinal);
    }
}

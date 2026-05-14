using System.Linq;
using ProEdit.Documents;
using Xunit;

namespace ProEdit.Html.Tests;

public class HtmlDualViewSyncTests
{
    [Fact]
    public void ApplyHtmlEdit_UpdatesDocumentAndText()
    {
        var sync = new HtmlDualViewSync();
        var state = sync.Initialize("<p>Hello</p>");

        var updated = sync.ApplyHtmlEdit(state, "<p>Hello</p><p>World</p>");

        Assert.Contains("World", updated.HtmlText, StringComparison.Ordinal);
        Assert.True(updated.Document.ParagraphCount >= 2);
    }

    [Fact]
    public void ApplyDocumentEdit_UpdatesHtmlText()
    {
        var sync = new HtmlDualViewSync();
        var state = sync.Initialize("<p>Hello</p>");

        var updated = sync.ApplyDocumentEdit(state, document =>
        {
            var paragraph = document.Blocks.OfType<ParagraphBlock>().FirstOrDefault();
            if (paragraph is null)
            {
                document.Blocks.Add(new ParagraphBlock("World"));
                return;
            }

            if (paragraph.Inlines.Count == 0)
            {
                paragraph.Inlines.Add(new RunInline("World"));
            }
            else
            {
                paragraph.Inlines.Add(new RunInline(" World"));
            }
        });

        Assert.Contains("World", updated.HtmlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveConflict_PrefersLastWriter()
    {
        var sync = new HtmlDualViewSync();
        var state = sync.Initialize("<p>Hello</p>");

        var htmlEdit = "<p>Html</p>";
        HtmlSyncState updated = sync.ResolveConflict(
            state,
            htmlEdit,
            document => document.Blocks.Add(new ParagraphBlock("Doc")),
            HtmlSyncSource.Html);

        Assert.Contains("Html", updated.HtmlText, StringComparison.Ordinal);
    }
}

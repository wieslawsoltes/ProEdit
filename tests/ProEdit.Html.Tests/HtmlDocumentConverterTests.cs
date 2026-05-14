using System.Text;
using ProEdit.Documents;
using Xunit;

namespace ProEdit.Html.Tests;

public class HtmlDocumentConverterTests
{
    [Fact]
    public void FromHtml_ParsesParagraphText()
    {
        var html = "<p>Hello <strong>world</strong></p>";

        var document = HtmlDocumentConverter.FromHtml(html.AsSpan(), new HtmlOptions());

        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        var text = GetInlineText(paragraph);
        Assert.Contains("Hello", text, StringComparison.Ordinal);
        Assert.Contains("world", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_SerializesDocument()
    {
        var document = new Document();
        document.Blocks.Clear();
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new RunInline("Hello"));
        document.Blocks.Add(paragraph);

        var html = HtmlDocumentConverter.ToHtml(document, new HtmlOptions());

        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_BasicHtml()
    {
        var html = "<p>Hello</p>";

        var document = HtmlDocumentConverter.FromHtml(html.AsSpan(), new HtmlOptions());
        var output = HtmlDocumentConverter.ToHtml(document, new HtmlOptions());

        Assert.Contains("Hello", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_List_PreservesItems()
    {
        var html = "<ul><li>One</li><li>Two</li></ul>";

        var document = HtmlDocumentConverter.FromHtml(html.AsSpan(), new HtmlOptions());
        var output = HtmlDocumentConverter.ToHtml(document, new HtmlOptions());

        Assert.Contains("<ul", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<li", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("One", output, StringComparison.Ordinal);
        Assert.Contains("Two", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_Table_PreservesCells()
    {
        var html = "<table><tr><td>A</td><td>B</td></tr></table>";

        var document = HtmlDocumentConverter.FromHtml(html.AsSpan(), new HtmlOptions());
        var output = HtmlDocumentConverter.ToHtml(document, new HtmlOptions());

        Assert.Contains("<table", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<td", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A", output, StringComparison.Ordinal);
        Assert.Contains("B", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_InlineStyles_PreservesBoldAndItalic()
    {
        var html = "<p><strong>Bold</strong> <em>Italic</em></p>";

        var document = HtmlDocumentConverter.FromHtml(html.AsSpan(), new HtmlOptions());
        var output = HtmlDocumentConverter.ToHtml(document, new HtmlOptions());

        Assert.Contains("Bold", output, StringComparison.Ordinal);
        Assert.Contains("Italic", output, StringComparison.Ordinal);
        Assert.Contains("font-weight", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("font-style", output, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetInlineText(ParagraphBlock paragraph)
    {
        var builder = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is RunInline run)
            {
                builder.Append(run.GetText());
            }
        }

        return builder.ToString();
    }
}

using Xunit;

namespace ProEdit.Markdown.Tests;

public class MarkdownRoundTripTests
{
    [Theory]
    [InlineData("# Title\n\nParagraph with *em* and **strong**.")]
    [InlineData("> Quote\n\n- One\n- Two")]
    [InlineData("```cs\nvar x = 1;\n```\n\n---")]
    public void RoundTrip_CommonMark(string markdown)
    {
        var options = new MarkdownOptions
        {
            Flavor = MarkdownFlavor.CommonMark,
            UseGfmTables = false,
            UseTaskLists = false,
            UseStrikethrough = false
        };

        var canonical = Canonicalize(markdown, options);
        var document = MarkdownDocumentConverter.FromMarkdown(markdown, options);
        var roundTrip = MarkdownDocumentConverter.ToMarkdown(document, options);

        Assert.Equal(canonical, roundTrip);
    }

    [Theory]
    [InlineData("| A | B |\n| --- | --- |\n| 1 | 2 |")]
    [InlineData("- [x] done\n- [ ] todo")]
    [InlineData("~~strike~~ and <https://example.com>")]
    public void RoundTrip_Gfm(string markdown)
    {
        var options = new MarkdownOptions
        {
            Flavor = MarkdownFlavor.GitHub,
            UseGfmTables = true,
            UseTaskLists = true,
            UseStrikethrough = true
        };

        var canonical = Canonicalize(markdown, options);
        var document = MarkdownDocumentConverter.FromMarkdown(markdown, options);
        var roundTrip = MarkdownDocumentConverter.ToMarkdown(document, options);

        Assert.Equal(canonical, roundTrip);
    }

    private static string Canonicalize(string markdown, MarkdownOptions options)
    {
        var parser = new MarkdownParser(options);
        var document = parser.Parse(markdown);
        var serializer = new MarkdownSerializer(options);
        return serializer.Serialize(document);
    }
}

using ProEdit.Markdown;
using ProEdit.Markdown.Ast;
using Xunit;

namespace ProEdit.Markdown.Tests;

public class MarkdownGfmTests
{
    [Fact]
    public void Parse_Strikethrough()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.GitHub, UseStrikethrough = true };
        var parser = new MarkdownParser(options);
        var document = parser.Parse("~~strike~~");
        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]);
        var strike = Assert.IsType<MarkdownStrikethroughInline>(paragraph.Inlines[0]);
        var text = Assert.IsType<MarkdownTextInline>(strike.Inlines[0]);
        Assert.Equal("strike", text.Text);
    }

    [Fact]
    public void Parse_TaskList()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.GitHub, UseTaskLists = true };
        var parser = new MarkdownParser(options);
        var document = parser.Parse("- [x] done");
        var list = Assert.IsType<MarkdownListBlock>(document.Blocks[0]);
        var item = Assert.IsType<MarkdownListItemBlock>(list.Items[0]);
        Assert.True(item.IsTask);
        Assert.True(item.TaskChecked);
    }

    [Fact]
    public void Parse_Table()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.GitHub, UseGfmTables = true };
        var parser = new MarkdownParser(options);
        var text = "| A | B |\n| --- | --- |\n| 1 | 2 |";
        var document = parser.Parse(text);
        var table = Assert.IsType<MarkdownTableBlock>(document.Blocks[0]);
        Assert.Equal(2, table.Rows.Count);
        Assert.True(table.HasHeader);
    }

    [Fact]
    public void Parse_AutoLink()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.GitHub };
        var parser = new MarkdownParser(options);
        var document = parser.Parse("<https://example.com>");
        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]);
        var link = Assert.IsType<MarkdownLinkInline>(paragraph.Inlines[0]);
        Assert.Equal("https://example.com", link.Url);
    }
}

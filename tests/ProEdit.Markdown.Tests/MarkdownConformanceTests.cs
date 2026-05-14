using ProEdit.Markdown.Ast;
using Xunit;

namespace ProEdit.Markdown.Tests;

public class MarkdownConformanceTests
{
    [Fact]
    public void CommonMark_Parses_AtxHeading()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var document = parser.Parse("# Title");
        var heading = Assert.IsType<MarkdownHeadingBlock>(document.Blocks[0]);
        Assert.Equal(1, heading.Level);
        Assert.Equal("Title", MarkdownTestHelpers.GetInlineText(heading.Inlines));
    }

    [Fact]
    public void CommonMark_Parses_SetextHeading()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var document = parser.Parse("Title\n---");
        var heading = Assert.IsType<MarkdownHeadingBlock>(document.Blocks[0]);
        Assert.Equal(2, heading.Level);
        Assert.Equal("Title", MarkdownTestHelpers.GetInlineText(heading.Inlines));
    }

    [Fact]
    public void CommonMark_Parses_EmphasisAndStrong()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var document = parser.Parse("*em* and **strong**");
        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]);
        Assert.Equal("em and strong", MarkdownTestHelpers.GetInlineText(paragraph.Inlines));
        var emphasis = Assert.IsType<MarkdownEmphasisInline>(paragraph.Inlines[0]);
        Assert.False(emphasis.IsStrong);
        var strong = Assert.IsType<MarkdownEmphasisInline>(paragraph.Inlines[2]);
        Assert.True(strong.IsStrong);
    }

    [Fact]
    public void CommonMark_Parses_CodeSpan()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var document = parser.Parse("Use `code` here");
        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]);
        var code = Assert.IsType<MarkdownCodeInline>(paragraph.Inlines[1]);
        Assert.Equal("code", code.Code);
    }

    [Fact]
    public void CommonMark_Parses_LinkAndImage()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var document = parser.Parse("[link](https://example.com \"Title\") ![alt](img.png)");
        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]);
        var link = Assert.IsType<MarkdownLinkInline>(paragraph.Inlines[0]);
        Assert.Equal("https://example.com", link.Url);
        Assert.Equal("Title", link.Title);
        var image = Assert.IsType<MarkdownImageInline>(paragraph.Inlines[2]);
        Assert.Equal("img.png", image.Url);
        Assert.Equal("alt", MarkdownTestHelpers.GetInlineText(image.AltText));
    }

    [Fact]
    public void CommonMark_Parses_BlockQuote()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var document = parser.Parse("> Quote");
        var quote = Assert.IsType<MarkdownBlockQuoteBlock>(document.Blocks[0]);
        var paragraph = Assert.IsType<MarkdownParagraphBlock>(quote.Blocks[0]);
        Assert.Equal("Quote", MarkdownTestHelpers.GetInlineText(paragraph.Inlines));
    }

    [Fact]
    public void CommonMark_Parses_List()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var document = parser.Parse("- One\n- Two");
        var list = Assert.IsType<MarkdownListBlock>(document.Blocks[0]);
        Assert.Equal(MarkdownListKind.Bullet, list.Kind);
        Assert.Equal(2, list.Items.Count);
        var itemParagraph = Assert.IsType<MarkdownParagraphBlock>(list.Items[0].Blocks[0]);
        Assert.Equal("One", MarkdownTestHelpers.GetInlineText(itemParagraph.Inlines));
    }

    [Fact]
    public void CommonMark_Parses_FencedCode()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var document = parser.Parse("```cs\nvar x = 1;\n```");
        var code = Assert.IsType<MarkdownCodeBlock>(document.Blocks[0]);
        Assert.True(code.IsFenced);
        Assert.Equal("cs", code.Info);
        Assert.Contains("var x = 1;", code.Text);
    }

    [Fact]
    public void CommonMark_Parses_ThematicBreak()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var document = parser.Parse("---");
        Assert.IsType<MarkdownThematicBreakBlock>(document.Blocks[0]);
    }

    [Fact]
    public void CommonMark_Parses_UnmatchedSpecialsAsText()
    {
        var parser = new MarkdownParser(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var text = "Hello [world *with _markers";
        var document = parser.Parse(text);
        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]);
        Assert.Equal(text, MarkdownTestHelpers.GetInlineText(paragraph.Inlines));
    }

    [Fact]
    public void Gfm_Parses_Table_TaskList_Strikethrough()
    {
        var options = new MarkdownOptions
        {
            Flavor = MarkdownFlavor.GitHub,
            UseGfmTables = true,
            UseTaskLists = true,
            UseStrikethrough = true
        };

        var parser = new MarkdownParser(options);
        var markdown = "| A | B |\n| --- | --- |\n| 1 | 2 |\n\n- [x] done\n\n~~strike~~";
        var document = parser.Parse(markdown);

        Assert.IsType<MarkdownTableBlock>(document.Blocks[0]);
        var list = Assert.IsType<MarkdownListBlock>(document.Blocks[1]);
        Assert.True(list.Items[0].IsTask);
        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[2]);
        Assert.IsType<MarkdownStrikethroughInline>(paragraph.Inlines[0]);
    }
}

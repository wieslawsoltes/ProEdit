using Vibe.Office.Documents;
using Xunit;

namespace Vibe.Office.Markdown.Tests;

public class MarkdownStyleTests
{
    [Fact]
    public void FromMarkdown_SeedsMarkdownStyles_ForCommonMark()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var document = MarkdownDocumentConverter.FromMarkdown("Hello", options);

        var paragraphStyles = new HashSet<string>(document.Styles.ParagraphStyles.Keys, StringComparer.OrdinalIgnoreCase);
        var expectedParagraph = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Normal",
            "Heading1",
            "Heading2",
            "Heading3",
            "Heading4",
            "Heading5",
            "Heading6",
            "BlockQuote",
            "CodeBlock",
            "ListParagraph",
            "TableCell",
            "TableHeader"
        };

        Assert.True(expectedParagraph.SetEquals(paragraphStyles));

        var characterStyles = new HashSet<string>(document.Styles.CharacterStyles.Keys, StringComparer.OrdinalIgnoreCase);

        var expectedCharacter = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CodeInline",
            "Hyperlink"
        };

        Assert.True(expectedCharacter.SetEquals(characterStyles));
        Assert.Empty(document.Styles.TableStyles);
        Assert.Equal("Normal", document.Styles.DefaultParagraphStyleId);
        Assert.Null(document.Styles.DefaultTableStyleId);
    }

    [Fact]
    public void FromMarkdown_SeedsMarkdownStyles_ForGitHub()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.GitHub, UseGfmTables = true };
        var document = MarkdownDocumentConverter.FromMarkdown("Hello", options);

        Assert.True(document.Styles.TableStyles.ContainsKey("MarkdownTable"));
        Assert.Equal("MarkdownTable", document.Styles.DefaultTableStyleId);
    }

    [Fact]
    public void FromMarkdown_Heading_SetsHeadingStyle()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var document = MarkdownDocumentConverter.FromMarkdown("# Title", options);

        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        Assert.Equal("Heading1", paragraph.StyleId);
        Assert.True(document.Styles.ParagraphStyles.ContainsKey("Heading1"));
    }

    [Fact]
    public void FromMarkdown_Table_CreatesStyledTableWithAlignment()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.GitHub, UseGfmTables = true };
        var markdown = "| A | B |\n| :-- | --: |\n| 1 | 2 |";
        var document = MarkdownDocumentConverter.FromMarkdown(markdown, options);

        var table = Assert.IsType<TableBlock>(document.Blocks[0]);
        Assert.Equal("MarkdownTable", table.StyleId);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);

        var left = Assert.IsType<ParagraphBlock>(table.Rows[0].Cells[0].Paragraphs[0]);
        var right = Assert.IsType<ParagraphBlock>(table.Rows[0].Cells[1].Paragraphs[0]);

        Assert.Equal(ParagraphAlignment.Left, left.Properties.Alignment);
        Assert.Equal(ParagraphAlignment.Right, right.Properties.Alignment);
        Assert.Equal("TableHeader", left.StyleId);
        Assert.Equal("TableHeader", right.StyleId);
    }

    [Fact]
    public void FromMarkdown_List_UsesListParagraphStyle()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var document = MarkdownDocumentConverter.FromMarkdown("- One\n- Two", options);

        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[0]);
        Assert.Equal("ListParagraph", paragraph.StyleId);
    }
}

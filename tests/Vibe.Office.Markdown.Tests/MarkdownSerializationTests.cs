using Vibe.Office.Documents;
using Vibe.Office.Markdown.Ast;
using Xunit;

namespace Vibe.Office.Markdown.Tests;

public class MarkdownSerializationTests
{
    [Fact]
    public void Serialize_CodeSpan_WithBackticks_RoundTrips()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var document = new MarkdownDocument(new MarkdownNodeId(1), MarkdownTextSpan.Unknown);
        var paragraph = new MarkdownParagraphBlock(new MarkdownNodeId(2), MarkdownTextSpan.Unknown);
        paragraph.Inlines.Add(new MarkdownTextInline(new MarkdownNodeId(3), MarkdownTextSpan.Unknown, "pre "));
        paragraph.Inlines.Add(new MarkdownCodeInline(new MarkdownNodeId(4), MarkdownTextSpan.Unknown, "``"));
        paragraph.Inlines.Add(new MarkdownTextInline(new MarkdownNodeId(5), MarkdownTextSpan.Unknown, " post"));
        document.Blocks.Add(paragraph);

        var serializer = new MarkdownSerializer(options);
        var markdown = serializer.Serialize(document);
        var parser = new MarkdownParser(options);
        var parsed = parser.Parse(markdown);

        var parsedParagraph = Assert.IsType<MarkdownParagraphBlock>(parsed.Blocks[0]);
        var code = Assert.IsType<MarkdownCodeInline>(parsedParagraph.Inlines[1]);
        Assert.Equal("``", code.Code);
    }

    [Fact]
    public void Serialize_CodeSpan_PreservesLeadingAndTrailingSpaces()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var document = new MarkdownDocument(new MarkdownNodeId(1), MarkdownTextSpan.Unknown);
        var paragraph = new MarkdownParagraphBlock(new MarkdownNodeId(2), MarkdownTextSpan.Unknown);
        paragraph.Inlines.Add(new MarkdownCodeInline(new MarkdownNodeId(3), MarkdownTextSpan.Unknown, " x "));
        document.Blocks.Add(paragraph);

        var serializer = new MarkdownSerializer(options);
        var markdown = serializer.Serialize(document);
        var parser = new MarkdownParser(options);
        var parsed = parser.Parse(markdown);

        var parsedParagraph = Assert.IsType<MarkdownParagraphBlock>(parsed.Blocks[0]);
        var code = Assert.IsType<MarkdownCodeInline>(parsedParagraph.Inlines[0]);
        Assert.Equal(" x ", code.Code);
    }

    [Fact]
    public void Serialize_TableCell_EscapesPipes()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.GitHub, UseGfmTables = true };
        var document = new MarkdownDocument(new MarkdownNodeId(1), MarkdownTextSpan.Unknown);
        var table = new MarkdownTableBlock(new MarkdownNodeId(2), MarkdownTextSpan.Unknown)
        {
            HasHeader = true
        };

        var header = new MarkdownTableRow(new MarkdownNodeId(3), MarkdownTextSpan.Unknown) { IsHeader = true };
        var headerCell = new MarkdownTableCell(new MarkdownNodeId(4), MarkdownTextSpan.Unknown);
        headerCell.Inlines.Add(new MarkdownTextInline(new MarkdownNodeId(5), MarkdownTextSpan.Unknown, "Header"));
        header.Cells.Add(headerCell);
        table.Rows.Add(header);

        var row = new MarkdownTableRow(new MarkdownNodeId(6), MarkdownTextSpan.Unknown);
        var cell = new MarkdownTableCell(new MarkdownNodeId(7), MarkdownTextSpan.Unknown);
        cell.Inlines.Add(new MarkdownTextInline(new MarkdownNodeId(8), MarkdownTextSpan.Unknown, "a|b"));
        row.Cells.Add(cell);
        table.Rows.Add(row);

        document.Blocks.Add(table);

        var serializer = new MarkdownSerializer(options);
        var markdown = serializer.Serialize(document);
        var parser = new MarkdownParser(options);
        var parsed = parser.Parse(markdown);

        var parsedTable = Assert.IsType<MarkdownTableBlock>(parsed.Blocks[0]);
        var dataCell = parsedTable.Rows[1].Cells[0];
        Assert.Equal("a|b", MarkdownTestHelpers.GetInlineText(dataCell.Inlines));
    }

    [Fact]
    public void ToMarkdown_UsesCodeInlineStyleId()
    {
        var document = new Document();
        document.Blocks.Clear();
        var paragraph = new ParagraphBlock();
        var run = new RunInline("code") { StyleId = "CodeInline" };
        paragraph.Inlines.Add(run);
        document.Blocks.Add(paragraph);

        var options = new MarkdownOptions
        {
            Flavor = MarkdownFlavor.CommonMark,
            UseGfmTables = false,
            UseTaskLists = false,
            UseStrikethrough = false
        };

        var markdown = MarkdownDocumentConverter.ToMarkdown(document, options);
        Assert.Contains("`code`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_EmitsTableAlignmentFromParagraphs()
    {
        var document = new Document();
        document.Blocks.Clear();

        var table = new TableBlock();
        var row = new TableRow();
        var leftParagraph = new ParagraphBlock("Left");
        leftParagraph.Properties.Alignment = ParagraphAlignment.Left;
        var rightParagraph = new ParagraphBlock("Right");
        rightParagraph.Properties.Alignment = ParagraphAlignment.Right;
        row.Cells.Add(new TableCell(new[] { leftParagraph }));
        row.Cells.Add(new TableCell(new[] { rightParagraph }));
        table.Rows.Add(row);
        document.Blocks.Add(table);

        var options = new MarkdownOptions { Flavor = MarkdownFlavor.GitHub, UseGfmTables = true };
        var markdown = MarkdownDocumentConverter.ToMarkdown(document, options);

        Assert.Contains("| :--- | ---: |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_DropsImplicitTrailingEmptyParagraph()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Hello"));
        document.Blocks.Add(new ParagraphBlock());

        var markdown = MarkdownDocumentConverter.ToMarkdown(document, new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });

        Assert.Equal("Hello", markdown);
    }

    [Fact]
    public void ToMarkdown_PreservesExplicitTrailingBlankLines()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new ParagraphBlock("Hello"));
        document.Blocks.Add(new ParagraphBlock());
        document.Blocks.Add(new ParagraphBlock());

        var markdown = MarkdownDocumentConverter.ToMarkdown(document, new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });

        Assert.Equal("Hello\n\n", markdown);
    }

    [Fact]
    public void Serialize_EmptyParagraphs_DoNotEmitContentWhenDocumentIsBlank()
    {
        var document = new MarkdownDocument(new MarkdownNodeId(1), MarkdownTextSpan.Unknown);
        document.Blocks.Add(new MarkdownParagraphBlock(new MarkdownNodeId(2), MarkdownTextSpan.Unknown));

        var serializer = new MarkdownSerializer(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var markdown = serializer.Serialize(document);

        Assert.Equal(string.Empty, markdown);
    }

    [Fact]
    public void Serialize_TrailingEmptyParagraphs_EmitTrailingBlankLines()
    {
        var document = new MarkdownDocument(new MarkdownNodeId(1), MarkdownTextSpan.Unknown);
        var paragraph = new MarkdownParagraphBlock(new MarkdownNodeId(2), MarkdownTextSpan.Unknown);
        paragraph.Inlines.Add(new MarkdownTextInline(new MarkdownNodeId(3), MarkdownTextSpan.Unknown, "Hello"));
        document.Blocks.Add(paragraph);
        document.Blocks.Add(new MarkdownParagraphBlock(new MarkdownNodeId(4), MarkdownTextSpan.Unknown));
        document.Blocks.Add(new MarkdownParagraphBlock(new MarkdownNodeId(5), MarkdownTextSpan.Unknown));

        var serializer = new MarkdownSerializer(new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark });
        var markdown = serializer.Serialize(document);

        Assert.Equal("Hello\n\n\n", markdown);
    }

    [Fact]
    public void Parse_PreservesTrailingBlankLines_WhenContentExists()
    {
        var options = new MarkdownOptions { Flavor = MarkdownFlavor.CommonMark };
        var parser = new MarkdownParser(options);
        var document = parser.Parse("Hello\n\n");
        var serializer = new MarkdownSerializer(options);

        var markdown = serializer.Serialize(document);

        Assert.Equal("Hello\n\n", markdown);
    }
}

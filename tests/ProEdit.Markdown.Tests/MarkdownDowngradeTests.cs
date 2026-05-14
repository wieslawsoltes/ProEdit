using System.Linq;
using ProEdit.Documents;
using ProEdit.Markdown.Ast;
using Xunit;

namespace ProEdit.Markdown.Tests;

public class MarkdownDowngradeTests
{
    [Fact]
    public void Downgrade_PageBreak_ConvertsToThematicBreak()
    {
        var document = new Document();
        document.Blocks.Clear();
        document.Blocks.Add(new PageBreakBlock());

        var report = new MarkdownConversionReport();
        var options = new MarkdownOptions();
        var result = MarkdownDowngradePass.Apply(document, options, report);

        var ast = MarkdownAstConverter.FromDocument(result.Document, options);

        Assert.True(ast.Blocks.OfType<MarkdownThematicBreakBlock>().Any());
        Assert.Equal(1, report.GetCount(MarkdownConversionFeature.PageBreak, MarkdownConversionAction.Converted));
    }

    [Fact]
    public void Downgrade_Image_ProducesMarkdownImageInline()
    {
        var document = new Document();
        document.Blocks.Clear();
        var paragraph = new ParagraphBlock();
        paragraph.Inlines.Add(new ImageInline(new byte[] { 1, 2, 3 }, 1f, 1f, "image/png"));
        document.Blocks.Add(paragraph);

        var report = new MarkdownConversionReport();
        var options = new MarkdownOptions { Downgrade = { EmbedImagesAsDataUri = true } };
        var result = MarkdownDowngradePass.Apply(document, options, report);

        var ast = MarkdownAstConverter.FromDocument(result.Document, options);
        var astParagraph = Assert.IsType<MarkdownParagraphBlock>(ast.Blocks[0]);
        var image = Assert.IsType<MarkdownImageInline>(astParagraph.Inlines[0]);

        Assert.StartsWith("data:image/png;base64,", image.Url, StringComparison.Ordinal);
        var altText = Assert.IsType<MarkdownTextInline>(image.AltText[0]);
        Assert.Equal("Image", altText.Text);
        Assert.Equal(1, report.GetCount(MarkdownConversionFeature.Image, MarkdownConversionAction.Converted));
    }
}

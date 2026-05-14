using ProEdit.WinUICompat.Bridges;
using ProEdit.WinUICompat.Documents;
using Xunit;

namespace ProEdit.WinUICompat.Tests;

public sealed class CompatDocumentBridgeTests
{
    [Fact]
    public void ToEditorDocument_ProducesParagraphContent()
    {
        var bridge = new CompatDocumentBridge();
        var source = new RichTextDocument();
        source.Blocks.Add(new Paragraph("bridge"));

        var editor = bridge.ToEditorDocument(source);

        Assert.NotNull(editor);
        Assert.NotEmpty(editor.Blocks);
    }

    [Fact]
    public void EmbeddedUiRoundtrip_PreservesMappedChildren()
    {
        var inlineChild = new EmbeddedTestChild();
        var blockChild = new EmbeddedTestChild();

        var bridge = new CompatDocumentBridge(new CompatDocumentBridgeOptions
        {
            EnableEmbeddedUiElements = true,
            EmbeddedUiElementPredicate = static child => child is EmbeddedTestChild,
            EmbeddedUiSizeResolver = static (child, _) => child is EmbeddedTestChild ? (96d, 28d) : null
        });

        var source = new RichTextDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new InlineUIContainer { Child = inlineChild });
        source.Blocks.Add(paragraph);
        source.Blocks.Add(new BlockUIContainer { Child = blockChild });

        var editor = bridge.ToEditorDocument(source);
        Assert.Equal(2, bridge.EmbeddedUiElementsById.Count);

        var roundtrip = bridge.FromEditorDocument(editor);
        var roundtripParagraph = Assert.IsType<Paragraph>(roundtrip.Blocks[0]);
        var roundtripInline = Assert.IsType<InlineUIContainer>(roundtripParagraph.Inlines[0]);
        var roundtripBlock = Assert.IsType<BlockUIContainer>(roundtrip.Blocks[1]);

        Assert.Same(inlineChild, roundtripInline.Child);
        Assert.Same(blockChild, roundtripBlock.Child);
    }

    private sealed class EmbeddedTestChild;
}

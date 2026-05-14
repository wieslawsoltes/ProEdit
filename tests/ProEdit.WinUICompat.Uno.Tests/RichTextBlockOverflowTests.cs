using System.Linq;
using ProEdit.WinUICompat.Controls;
using ProEdit.WinUICompat.Documents;
using Xunit;

namespace ProEdit.WinUICompat.Uno.Tests;

public sealed class RichTextBlockOverflowTests
{
    [Fact]
    public void RichTextBlock_WithOverflowTarget_UpdatesOverflowState()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var source = new RichTextBlock
        {
            MaxLines = 1
        };
        var overflow = new RichTextBlockOverflow();

        source.OverflowContentTarget = overflow;
        source.Blocks.Add(new Paragraph(string.Join(' ', Enumerable.Repeat("word", 200))));
        source.RefreshOverflowLayout();

        Assert.True(source.HasOverflowContent);
        Assert.True(overflow.HasOverflowContent);
        Assert.Same(source, overflow.ContentSource);
        Assert.Same(source, overflow.Source);
    }

    [Fact]
    public void RichTextBlock_WithoutOverflowTarget_DoesNotReportOverflow()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var source = new RichTextBlock
        {
            MaxLines = 1
        };

        source.Blocks.Add(new Paragraph(string.Join(' ', Enumerable.Repeat("word", 200))));
        source.RefreshOverflowLayout();

        Assert.False(source.HasOverflowContent);
    }

    [Fact]
    public void RichTextBlockOverflow_PropagatesToOverflowChain()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var source = new RichTextBlock
        {
            MaxLines = 1
        };
        var overflow1 = new RichTextBlockOverflow();
        var overflow2 = new RichTextBlockOverflow();

        source.OverflowContentTarget = overflow1;
        overflow1.OverflowContentTarget = overflow2;
        source.Blocks.Add(new Paragraph(string.Join(' ', Enumerable.Repeat("word", 200))));
        source.RefreshOverflowLayout();

        Assert.True(overflow1.HasOverflowContent);
        Assert.True(overflow2.HasOverflowContent);
        Assert.Same(overflow1, overflow2.ContentSource);
    }
}

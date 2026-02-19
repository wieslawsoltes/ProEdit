using Vibe.Office.WinUICompat.Controls;
using Xunit;

namespace Vibe.Office.WinUICompat.Uno.Tests;

public sealed class RichTextBlockApiTests
{
    [Fact]
    public void RichTextBlock_ExposesOverflowApiSurface()
    {
        var type = typeof(RichTextBlock);

        Assert.NotNull(type.GetProperty(nameof(RichTextBlock.Document)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBlock.Blocks)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBlock.OverflowContentTarget)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBlock.MaxLines)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBlock.HasOverflowContent)));
        Assert.NotNull(type.GetMethod(nameof(RichTextBlock.RefreshOverflowLayout)));
    }

    [Fact]
    public void RichTextBlockOverflow_ExposesExpectedProperties()
    {
        var type = typeof(RichTextBlockOverflow);

        Assert.NotNull(type.GetProperty(nameof(RichTextBlockOverflow.OverflowContentTarget)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBlockOverflow.ContentSource)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBlockOverflow.Source)));
        Assert.NotNull(type.GetProperty(nameof(RichTextBlockOverflow.HasOverflowContent)));
    }
}

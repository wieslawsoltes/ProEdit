using Vibe.Office.WinUICompat.Controls;
using Microsoft.UI.Xaml;
using Xunit;

namespace Vibe.Office.WinUICompat.Uno.Tests;

public sealed class RichEditBoxApiTests
{
    [Fact]
    public void RichEditBox_ExposesExpectedApiSurface()
    {
        var type = typeof(RichEditBox);

        Assert.NotNull(type.GetProperty(nameof(RichEditBox.Document)));
        Assert.NotNull(type.GetProperty(nameof(RichEditBox.TextDocument)));
        Assert.NotNull(type.GetProperty(nameof(RichEditBox.Selection)));
        Assert.NotNull(type.GetProperty(nameof(RichEditBox.CaretPosition)));

        Assert.NotNull(type.GetMethod(nameof(RichEditBox.Copy)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.Cut)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.Paste)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.Undo)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.Redo)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.SelectAll)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.Select)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.BeginChange)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.EndChange)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.GetPositionFromPoint)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.GetSpellingError)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.GetSpellingErrorRange)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.GetNextSpellingErrorPosition)));
        Assert.NotNull(type.GetMethod(nameof(RichEditBox.ShouldSerializeDocument)));

        var textChanged = type.GetEvent(nameof(RichEditBox.TextChanged));
        var selectionChanged = type.GetEvent(nameof(RichEditBox.SelectionChanged));
        Assert.NotNull(textChanged);
        Assert.NotNull(selectionChanged);
        Assert.Equal(typeof(RoutedEventHandler), textChanged!.EventHandlerType);
        Assert.Equal(typeof(RoutedEventHandler), selectionChanged!.EventHandlerType);
    }
}

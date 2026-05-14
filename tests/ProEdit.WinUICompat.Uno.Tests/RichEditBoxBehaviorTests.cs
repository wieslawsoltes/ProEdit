using ProEdit.WinUICompat.Controls;
using ProEdit.WinUICompat.Text;
using Windows.Foundation;
using ProEdit.WinUICompat.Documents;
using Xunit;

namespace ProEdit.WinUICompat.Uno.Tests;

public sealed class RichEditBoxBehaviorTests
{
    [Fact]
    public void ReplaceText_ThenSelectAll_SelectsWholeContent()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();
        box.ReplaceText("hello");

        box.SelectAll();

        Assert.Equal(0, box.Selection.Start.Offset);
        Assert.Equal(5, box.Selection.End.Offset);
    }

    [Fact]
    public void UndoRedo_ProxiesToDocument()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();
        box.ReplaceText("one");
        box.ReplaceText("two");

        Assert.True(box.Undo());
        Assert.Equal("one", box.Document.GetText());

        Assert.True(box.Redo());
        Assert.Equal("two", box.Document.GetText());
    }

    [Fact]
    public void CopyPaste_UsesControlSelection()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();
        box.ReplaceText("abcdef");
        box.Select(new TextPointer(0, 1), new TextPointer(0, 4));

        Assert.True(box.Copy());
        box.CaretPosition = new TextPointer(0, 6);
        Assert.True(box.Paste());

        Assert.Equal("abcdefbcd", box.Document.GetText());
    }

    [Fact]
    public void GetPositionFromPoint_SnapDisabled_OutsideTextReturnsNull()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();
        box.ReplaceText("hello");

        var pointer = box.GetPositionFromPoint(new Point(-1, 0), snapToText: false);

        Assert.Null(pointer);
    }

    [Fact]
    public void SpellingMethods_ReturnDiagnosticsByPosition()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox
        {
            IsProofingEnabled = true,
            IsSpellingEnabled = true
        };

        box.ReplaceText("helo world tezt");
        box.SetProofingDiagnostics(
            new[]
            {
                new ProofingDiagnostic(ProofingIssueKind.Spelling, 0, 1, 3, "spelling"),
                new ProofingDiagnostic(ProofingIssueKind.Spelling, 0, 11, 4, "spelling")
            });

        var first = box.GetSpellingError(new TextPointer(0, 2));
        var next = box.GetNextSpellingErrorPosition(new TextPointer(0, 2), LogicalDirection.Forward);
        var range = box.GetSpellingErrorRange(new TextPointer(0, 2));

        Assert.True(first.HasValue);
        Assert.Equal(1, first.Value.StartOffset);
        Assert.NotNull(next);
        Assert.Equal(11, next!.Offset);
        Assert.NotNull(range);
        Assert.Equal(1, range!.Start.Offset);
        Assert.Equal(4, range.End.Offset);
    }

    [Fact]
    public void ShouldSerializeDocument_TracksImplicitAndExplicitStates()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var implicitEmpty = new RichEditBox();
        Assert.False(implicitEmpty.ShouldSerializeDocument());

        implicitEmpty.ReplaceText("x");
        Assert.True(implicitEmpty.ShouldSerializeDocument());

        var explicitEmpty = new RichEditBox();
        explicitEmpty.Document = new RichEditTextDocument();
        Assert.True(explicitEmpty.ShouldSerializeDocument());
    }

    [Fact]
    public void Select_UsesParagraphAwarePointers()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();
        box.ReplaceText("ab\ncd");

        box.Select(new TextPointer(1, 0), new TextPointer(1, 2));

        Assert.Equal(1, box.Selection.Start.ParagraphIndex);
        Assert.Equal(0, box.Selection.Start.Offset);
        Assert.Equal(1, box.Selection.End.ParagraphIndex);
        Assert.Equal(2, box.Selection.End.Offset);
    }
}

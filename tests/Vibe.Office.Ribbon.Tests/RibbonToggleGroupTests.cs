using Vibe.Office.Ribbon;
using Xunit;

namespace Vibe.Office.Ribbon.Tests;

public sealed class RibbonToggleGroupTests
{
    [Fact]
    public void RefreshState_PropagatesToItems()
    {
        var canExecute = false;
        var toggle = new RibbonToggleButton(
            "align-left",
            "Align Left",
            () => false,
            command: new RibbonCommand(() => { }),
            canExecute: () => canExecute,
            size: RibbonControlSize.Small);

        var group = new RibbonToggleGroup(
            "align",
            "Alignment",
            new[] { toggle },
            columns: 1,
            size: RibbonControlSize.Medium);

        group.RefreshState();
        Assert.False(toggle.IsEnabled);

        canExecute = true;
        group.RefreshState();
        Assert.True(toggle.IsEnabled);
    }
}

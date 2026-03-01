using System.Reflection;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Vibe.Office.WinUICompat.Controls;
using Xunit;

namespace Vibe.Office.WinUICompat.Uno.Tests;

public sealed class RichEditBoxAutomationTests
{
    [Fact]
    public void RichEditBox_ExposesValueAutomationPattern()
    {
        if (!UnoUiTestEnvironment.IsUiRuntimeAvailable())
        {
            return;
        }

        var box = new RichEditBox();
        box.ReplaceText("automation");

        var createPeer = typeof(RichEditBox).GetMethod(
            "OnCreateAutomationPeer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(createPeer);

        var peer = Assert.IsAssignableFrom<AutomationPeer>(createPeer!.Invoke(box, null));
        var provider = peer.GetPattern(PatternInterface.Value);
        var valueProvider = Assert.IsAssignableFrom<IValueProvider>(provider);
        Assert.Equal("automation", valueProvider.Value);
    }
}

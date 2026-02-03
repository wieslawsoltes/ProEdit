using System.Threading.Tasks;
using Vibe.Office.Ribbon;
using Xunit;

namespace Vibe.Office.Ribbon.Tests;

public sealed class RibbonTextBoxTests
{
    [Fact]
    public async Task SubmitAsync_InvokesHandlerWithCurrentText()
    {
        string? submitted = null;
        var textBox = new RibbonTextBox(
            "search",
            "Search",
            text: "hello",
            submitHandler: value =>
            {
                submitted = value;
                return ValueTask.CompletedTask;
            });

        await textBox.SubmitAsync();

        Assert.Equal("hello", submitted);
    }

    [Fact]
    public void RefreshState_UpdatesTextWithoutInvokingHandler()
    {
        var source = "alpha";
        var changeCount = 0;
        var textBox = new RibbonTextBox(
            "search",
            "Search",
            textEvaluator: () => source,
            textChangedHandler: _ =>
            {
                changeCount++;
                return ValueTask.CompletedTask;
            });

        Assert.Equal("alpha", textBox.Text);

        source = "beta";
        textBox.RefreshState();

        Assert.Equal("beta", textBox.Text);
        Assert.Equal(0, changeCount);
    }
}

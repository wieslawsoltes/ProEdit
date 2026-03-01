using Vibe.Office.FlowDocument.IO;
using Xunit;

namespace Vibe.Office.FlowDocument.IO.Tests;

public sealed class PostScriptRuntimeOptionsTests
{
    [Fact]
    public void ApplyOverrides_UsesGhostscriptPathAndTimeout()
    {
        var options = new PostScriptConversionOptions
        {
            GhostscriptPath = "gs",
            ProcessTimeout = TimeSpan.FromSeconds(30)
        };

        var result = PostScriptRuntimeOptions.ApplyOverrides(options, " /custom/gs ", "90.5");

        Assert.Same(options, result);
        Assert.Equal("/custom/gs", options.GhostscriptPath);
        Assert.Equal(TimeSpan.FromSeconds(90.5), options.ProcessTimeout);
    }

    [Fact]
    public void ApplyOverrides_IgnoresInvalidTimeoutAndWhitespacePath()
    {
        var options = new PostScriptConversionOptions
        {
            GhostscriptPath = "gs",
            ProcessTimeout = TimeSpan.FromSeconds(30)
        };

        PostScriptRuntimeOptions.ApplyOverrides(options, " ", "-1");

        Assert.Equal("gs", options.GhostscriptPath);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ProcessTimeout);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2.5", 2.5)]
    [InlineData(" 3 ", 3)]
    public void TryParseTimeoutSeconds_ReturnsExpectedValues(string input, double expectedSeconds)
    {
        var parsed = PostScriptRuntimeOptions.TryParseTimeoutSeconds(input, out var timeout);

        Assert.True(parsed);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), timeout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void TryParseTimeoutSeconds_RejectsInvalidValues(string? input)
    {
        var parsed = PostScriptRuntimeOptions.TryParseTimeoutSeconds(input, out var timeout);

        Assert.False(parsed);
        Assert.Equal(default, timeout);
    }
}

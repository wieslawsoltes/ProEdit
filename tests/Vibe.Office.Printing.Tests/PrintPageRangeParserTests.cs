using Vibe.Office.Printing;
using Xunit;

namespace Vibe.Office.Printing.Tests;

public sealed class PrintPageRangeParserTests
{
    [Fact]
    public void TryParse_ValidRanges_ReturnsNormalizedRanges()
    {
        var success = PrintPageRangeParser.TryParse("1-3, 5, 4", out var ranges);

        Assert.True(success);
        Assert.Single(ranges);
        Assert.Equal(new PrintPageRange(1, 5), ranges[0]);
    }

    [Fact]
    public void TryParse_InvalidRange_ReturnsFalse()
    {
        var success = PrintPageRangeParser.TryParse("0, -1, a", out var ranges);

        Assert.False(success);
        Assert.Empty(ranges);
    }

    [Fact]
    public void ToDisplayString_MergesRanges()
    {
        var ranges = new[]
        {
            new PrintPageRange(1, 2),
            new PrintPageRange(3, 4),
            new PrintPageRange(6, 6)
        };

        var display = PrintPageRangeParser.ToDisplayString(ranges);

        Assert.Equal("1-4, 6", display);
    }
}

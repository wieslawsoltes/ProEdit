using ProEdit.Printing;
using Xunit;

namespace ProEdit.Printing.Tests;

public sealed class PrintSettingsTests
{
    [Fact]
    public void Clone_CopiesPaperSizeAndRanges()
    {
        var size = new PrintPaperSize("Letter", 816f, 1056f);
        var settings = new PrintSettings
        {
            OutputKind = PrintOutputKind.Printer,
            Copies = 2,
            RangeKind = PrintRangeKind.CustomPages,
            CustomRanges = new[] { new PrintPageRange(1, 3) },
            PaperSize = size,
            CustomScale = 1.25f
        };

        var clone = settings.Clone();

        Assert.Equal(settings.OutputKind, clone.OutputKind);
        Assert.Equal(settings.Copies, clone.Copies);
        Assert.Equal(settings.RangeKind, clone.RangeKind);
        Assert.Equal(settings.CustomRanges, clone.CustomRanges);
        Assert.Equal(settings.PaperSize, clone.PaperSize);
        Assert.Equal(settings.CustomScale, clone.CustomScale);
    }

    [Fact]
    public void Normalize_ClampsAndClearsCustomRanges()
    {
        var settings = new PrintSettings
        {
            Copies = 0,
            CustomScale = 0.01f,
            RangeKind = PrintRangeKind.All,
            CustomRanges = new[] { new PrintPageRange(1, 2) }
        };

        settings.Normalize();

        Assert.Equal(1, settings.Copies);
        Assert.True(settings.CustomScale >= 0.1f);
        Assert.Empty(settings.CustomRanges);
    }
}

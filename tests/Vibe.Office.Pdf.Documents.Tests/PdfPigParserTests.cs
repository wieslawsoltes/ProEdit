using System;
using UglyToad.PdfPig.Graphics.Colors;
using Vibe.Office.Pdf.PdfPig;
using Xunit;

namespace Vibe.Office.Pdf.Documents.Tests;

public sealed class PdfPigParserTests
{
    [Fact]
    public void ToPdfColorIgnoresPatternColors()
    {
        var color = new ThrowingColor();

        var result = PdfPigParser.ToPdfColor(color);

        Assert.Null(result);
    }

    private sealed class ThrowingColor : IColor
    {
        public ColorSpace ColorSpace => ColorSpace.DeviceRGB;

        public (double r, double g, double b) ToRGBValues()
        {
            throw new InvalidOperationException("Cannot call ToRGBValues in a Pattern color.");
        }
    }
}

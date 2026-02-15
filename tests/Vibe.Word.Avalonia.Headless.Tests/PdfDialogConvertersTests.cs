using System.Globalization;
using Avalonia;
using Vibe.Office.Pdf;
using Vibe.Word.Avalonia;
using Xunit;

namespace Vibe.Word.Avalonia.Headless.Tests;

public sealed class PdfDialogConvertersTests
{
    [Fact]
    public void EnumEqualsConverterConvertBack_CheckedValueMapsToEnum()
    {
        var converter = new EnumEqualsConverter();

        var result = converter.ConvertBack(
            true,
            typeof(PdfImportMode),
            "FixedLayout",
            CultureInfo.InvariantCulture);

        Assert.Equal(PdfImportMode.FixedLayout, result);
    }

    [Fact]
    public void EnumEqualsConverterConvertBack_UncheckedValueReturnsUnset()
    {
        var converter = new EnumEqualsConverter();

        var result = converter.ConvertBack(
            false,
            typeof(PdfImportMode),
            "Reflow",
            CultureInfo.InvariantCulture);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }

    [Fact]
    public void EnumEqualsConverterConvert_InvalidEnumParameterDoesNotThrow()
    {
        var converter = new EnumEqualsConverter();

        var result = converter.Convert(
            PdfImportMode.Reflow,
            typeof(bool),
            "InvalidEnumValue",
            CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void EnumEqualsConverterConvertBack_InvalidEnumParameterReturnsUnset()
    {
        var converter = new EnumEqualsConverter();

        var result = converter.ConvertBack(
            true,
            typeof(PdfImportMode),
            "InvalidEnumValue",
            CultureInfo.InvariantCulture);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }
}

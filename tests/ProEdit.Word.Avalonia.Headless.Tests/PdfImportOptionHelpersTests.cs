using ProEdit.Pdf;
using ProEdit.Word.Avalonia;
using Xunit;

namespace ProEdit.Word.Avalonia.Headless.Tests;

public sealed class PdfImportOptionHelpersTests
{
    [Theory]
    [InlineData("sample.pdf")]
    [InlineData("sample.PDF")]
    [InlineData("sample.pdx")]
    [InlineData("sample.PDX")]
    [InlineData(".pdf")]
    [InlineData(".PDX")]
    public void IsPdfPath_ReturnsTrue_ForPdfAndPdx(string path)
    {
        Assert.True(PdfImportOptionHelpers.IsPdfPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sample.docx")]
    [InlineData("sample.md")]
    public void IsPdfPath_ReturnsFalse_ForOtherValues(string? path)
    {
        Assert.False(PdfImportOptionHelpers.IsPdfPath(path));
    }

    [Fact]
    public void Create_NormalizesInvalidEnumValues()
    {
        var options = PdfImportOptionHelpers.Create(
            (PdfImportMode)999,
            (PdfPreservationMode)999);

        Assert.Equal(PdfImportMode.Reflow, options.Mode);
        Assert.Equal(PdfPreservationMode.None, options.PreservationMode);
    }

    [Fact]
    public void Create_EnablesPreserveSourceBytes_WhenPreservationRequested()
    {
        var options = PdfImportOptionHelpers.Create(
            PdfImportMode.Reflow,
            PdfPreservationMode.StoreOriginal);

        Assert.True(options.ParserOptions.PreserveSourceBytes);
    }

    [Fact]
    public void Create_EnablesFixedLayoutParserOptions_ForFixedLayoutMode()
    {
        var options = PdfImportOptionHelpers.Create(
            PdfImportMode.FixedLayout,
            PdfPreservationMode.None);

        Assert.True(options.ParserOptions.ExtractPaths);
        Assert.True(options.ParserOptions.NormalizeFontNames);
    }
}

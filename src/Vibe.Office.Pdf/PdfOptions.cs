namespace Vibe.Office.Pdf;

public sealed class PdfParserOptions
{
    public bool PreserveSourceBytes { get; set; }
    public bool NormalizeFontNames { get; set; } = true;
    public bool ExtractTextGlyphs { get; set; } = true;
    public bool ExtractPaths { get; set; } = true;
    public bool ExtractEmbeddedFonts { get; set; } = true;
}

public sealed class PdfImportOptions
{
    public PdfImportMode Mode { get; set; } = PdfImportMode.Reflow;
    public PdfPreservationMode PreservationMode { get; set; } = PdfPreservationMode.None;
    public PdfParserOptions ParserOptions { get; } = new PdfParserOptions();

    public bool ShouldPreserveSource => PreservationMode != PdfPreservationMode.None || ParserOptions.PreserveSourceBytes;
}

public sealed class PdfWriteOptions
{
    public PdfPreservationMode PreservationMode { get; set; } = PdfPreservationMode.None;
}

public sealed class PdfExportOptions
{
    public PdfExportMode ExportMode { get; set; } = PdfExportMode.Regenerate;
    public bool AllowPreserveWithChanges { get; set; }
}

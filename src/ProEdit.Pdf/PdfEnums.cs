namespace ProEdit.Pdf;

public enum PdfImportMode
{
    Reflow,
    FixedLayout
}

public enum PdfPreservationMode
{
    None,
    StoreOriginal,
    Incremental
}

public enum PdfExportMode
{
    Regenerate,
    Preserve
}

public enum PdfIncrementalOverlayKind
{
    Text,
    Image
}

public enum PdfImageEncoding
{
    Jpeg
}

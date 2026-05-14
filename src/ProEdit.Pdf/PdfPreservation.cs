namespace ProEdit.Pdf;

public sealed class PdfPreservationManifest
{
    public int Version { get; set; } = 1;
    public string? ParserProviderId { get; set; }
    public string? WriterProviderId { get; set; }
    public PdfImportMode ImportMode { get; set; } = PdfImportMode.Reflow;
    public PdfPreservationMode PreservationMode { get; set; } = PdfPreservationMode.None;
    public string? ContentHash { get; set; }
    public int PageCount { get; set; }
    public PdfObjectMap ObjectMap { get; } = new();
}

public sealed class PdfObjectMap
{
    public List<PdfPageObjectMap> Pages { get; } = new();
}

public sealed class PdfPageObjectMap
{
    public int PageIndex { get; set; }
    public List<PdfMappedObject> Objects { get; } = new();
}

public sealed class PdfMappedObject
{
    public string ObjectId { get; set; } = string.Empty;
    public PdfMappedObjectKind Kind { get; set; } = PdfMappedObjectKind.Unknown;
    public PdfRect Bounds { get; set; }
}

public enum PdfMappedObjectKind
{
    Unknown,
    Text,
    Image,
    Shape
}

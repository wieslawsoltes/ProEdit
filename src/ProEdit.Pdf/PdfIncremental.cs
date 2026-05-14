namespace ProEdit.Pdf;

public sealed class PdfIncrementalUpdatePlan
{
    public bool CanApply { get; set; }
    public bool HasChanges { get; set; }
    public List<string> Issues { get; } = new();
    public List<PdfIncrementalOverlay> Overlays { get; } = new();
}

public sealed class PdfIncrementalOverlay
{
    public int PageIndex { get; set; }
    public PdfRect Bounds { get; set; }
    public string? Description { get; set; }
    public string? Text { get; set; }
    public PdfIncrementalOverlayKind Kind { get; set; } = PdfIncrementalOverlayKind.Text;
    public byte[]? ImageBytes { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public PdfImageEncoding ImageEncoding { get; set; } = PdfImageEncoding.Jpeg;
}

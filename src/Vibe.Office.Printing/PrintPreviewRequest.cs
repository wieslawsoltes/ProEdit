namespace Vibe.Office.Printing;

public sealed class PrintPreviewRequest
{
    public PrintPreviewRequest(IPrintDocumentInfo documentInfo, PrintSettings settings)
    {
        DocumentInfo = documentInfo ?? throw new ArgumentNullException(nameof(documentInfo));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public IPrintDocumentInfo DocumentInfo { get; }
    public PrintSettings Settings { get; }
    public IReadOnlyList<int>? PageIndices { get; set; }
    public float Dpi { get; set; } = 120f;
}

namespace ProEdit.Printing;

public sealed class PrintPreviewResult
{
    public PrintPreviewResult(int totalPages, IReadOnlyList<int> printablePageIndices, IReadOnlyList<PrintPreviewPage> pages)
    {
        TotalPages = Math.Max(0, totalPages);
        PrintablePageIndices = printablePageIndices ?? Array.Empty<int>();
        Pages = pages ?? Array.Empty<PrintPreviewPage>();
    }

    public int TotalPages { get; }
    public IReadOnlyList<int> PrintablePageIndices { get; }
    public IReadOnlyList<PrintPreviewPage> Pages { get; }
}

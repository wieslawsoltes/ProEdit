namespace ProEdit.Printing;

public interface IPrintService
{
    ValueTask<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default);

    ValueTask<PrinterInfo?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default);

    ValueTask<PrintPreviewResult> BuildPreviewAsync(PrintPreviewRequest request, CancellationToken cancellationToken = default);

    ValueTask<PrintJobResult> PrintAsync(IPrintDocumentInfo documentInfo, PrintSettings settings, CancellationToken cancellationToken = default);
}

public interface IPrinterDiscovery
{
    ValueTask<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default);

    ValueTask<PrinterInfo?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default);
}

public interface IPrintTransport
{
    ValueTask<PrintJobResult> SendToPrinterAsync(string pdfPath, PrintSettings settings, CancellationToken cancellationToken = default);
}

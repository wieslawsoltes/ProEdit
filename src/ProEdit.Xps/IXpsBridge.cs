namespace ProEdit.FlowDocument.IO;

/// <summary>
/// Converts between XPS/OXPS and PDF files.
/// </summary>
public interface IXpsBridge
{
    /// <summary>
    /// Converts XPS or OXPS input to PDF.
    /// </summary>
    Task ConvertXpsToPdfAsync(
        string sourcePath,
        string targetPdfPath,
        XpsFlavor flavor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts PDF input to XPS or OXPS output.
    /// </summary>
    Task ConvertPdfToXpsAsync(
        string sourcePdfPath,
        string targetPath,
        XpsFlavor flavor,
        CancellationToken cancellationToken = default);
}

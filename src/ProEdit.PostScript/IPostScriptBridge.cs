namespace ProEdit.FlowDocument.IO;

/// <summary>
/// Converts between PostScript (PS/EPS) and PDF files.
/// </summary>
public interface IPostScriptBridge
{
    /// <summary>
    /// Converts PS or EPS input to PDF.
    /// </summary>
    /// <param name="sourcePath">Source PostScript path.</param>
    /// <param name="targetPdfPath">Target PDF path.</param>
    /// <param name="kind">PostScript kind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConvertPostScriptToPdfAsync(
        string sourcePath,
        string targetPdfPath,
        PostScriptKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts PDF input to PS or EPS output.
    /// </summary>
    /// <param name="sourcePdfPath">Source PDF path.</param>
    /// <param name="targetPath">Target PostScript path.</param>
    /// <param name="kind">PostScript kind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConvertPdfToPostScriptAsync(
        string sourcePdfPath,
        string targetPath,
        PostScriptKind kind,
        CancellationToken cancellationToken = default);
}

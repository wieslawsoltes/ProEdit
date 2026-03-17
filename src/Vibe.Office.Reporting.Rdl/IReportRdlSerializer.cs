namespace Vibe.Office.Reporting.Rdl;

/// <summary>
/// Loads and saves the supported RDL subset used by VibeOffice reporting.
/// </summary>
public interface IReportRdlSerializer
{
    /// <summary>
    /// Reads a report definition from RDL XML text.
    /// </summary>
    /// <param name="xml">The RDL payload.</param>
    /// <returns>The read result.</returns>
    ReportRdlReadResult Read(string xml);

    /// <summary>
    /// Reads a report definition from an RDL XML stream.
    /// </summary>
    /// <param name="stream">The input stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read result.</returns>
    ValueTask<ReportRdlReadResult> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a report definition to RDL XML text.
    /// </summary>
    /// <param name="reportDefinition">The report definition to serialize.</param>
    /// <param name="options">Optional RDL serialization options.</param>
    /// <returns>The write result.</returns>
    ReportRdlWriteResult Write(
        ReportDefinition reportDefinition,
        ReportRdlWriteOptions? options = null);

    /// <summary>
    /// Writes a report definition to an RDL XML stream.
    /// </summary>
    /// <param name="reportDefinition">The report definition to serialize.</param>
    /// <param name="stream">The target output stream.</param>
    /// <param name="options">Optional RDL serialization options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The write result.</returns>
    ValueTask<ReportRdlWriteResult> WriteAsync(
        ReportDefinition reportDefinition,
        Stream stream,
        ReportRdlWriteOptions? options = null,
        CancellationToken cancellationToken = default);
}

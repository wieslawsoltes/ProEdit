namespace ProEdit.Reporting.Serialization;

/// <summary>
/// Loads and saves native ProEdit report templates.
/// </summary>
public interface IReportTemplateSerializer
{
    /// <summary>
    /// Reads a report template from JSON text.
    /// </summary>
    /// <param name="json">The JSON payload.</param>
    /// <returns>The read result.</returns>
    ReportTemplateReadResult Read(string json);

    /// <summary>
    /// Reads a report template from a stream.
    /// </summary>
    /// <param name="stream">The input stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read result.</returns>
    ValueTask<ReportTemplateReadResult> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a report template to JSON text.
    /// </summary>
    /// <param name="reportDefinition">The report definition.</param>
    /// <returns>The write result.</returns>
    ReportTemplateWriteResult Write(ReportDefinition reportDefinition);

    /// <summary>
    /// Writes a report template to a stream.
    /// </summary>
    /// <param name="reportDefinition">The report definition.</param>
    /// <param name="stream">The output stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The write result.</returns>
    ValueTask<ReportTemplateWriteResult> WriteAsync(
        ReportDefinition reportDefinition,
        Stream stream,
        CancellationToken cancellationToken = default);
}

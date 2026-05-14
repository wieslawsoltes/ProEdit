using ProEdit.Layout;

namespace ProEdit.Reporting.Export;

/// <summary>
/// Supported report export formats.
/// </summary>
public enum ReportExportFormat
{
    /// <summary>
    /// Portable Document Format.
    /// </summary>
    Pdf,

    /// <summary>
    /// Office Open XML word-processing document.
    /// </summary>
    Docx,

    /// <summary>
    /// HyperText Markup Language.
    /// </summary>
    Html,

    /// <summary>
    /// Rich Text Format.
    /// </summary>
    Rtf,

    /// <summary>
    /// Markdown text.
    /// </summary>
    Markdown,

    /// <summary>
    /// XML Paper Specification.
    /// </summary>
    Xps,

    /// <summary>
    /// PostScript.
    /// </summary>
    Ps,

    /// <summary>
    /// Comma-separated values.
    /// </summary>
    Csv,

    /// <summary>
    /// Office Open XML spreadsheet workbook.
    /// </summary>
    Xlsx
}

/// <summary>
/// Base type for report export profile options.
/// </summary>
public abstract class ReportExportProfile
{
}

/// <summary>
/// Configures document-based paginated exports.
/// </summary>
public sealed class PaginatedReportExportProfile : ReportExportProfile
{
    /// <summary>
    /// Gets or sets optional layout settings used for PDF, XPS, and PostScript export.
    /// </summary>
    public LayoutSettings? LayoutSettings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether HTML output should be pretty-printed.
    /// </summary>
    public bool PrettyPrintHtml { get; set; }
}

/// <summary>
/// Base type for semantic tablix exports.
/// </summary>
public abstract class TabularReportExportProfile : ReportExportProfile
{
    /// <summary>
    /// Gets or sets the tablix item identifier to export.
    /// </summary>
    public string? TablixItemId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether materialized header rows should be included.
    /// </summary>
    public bool IncludeHeaderRows { get; set; } = true;
}

/// <summary>
/// Configures CSV export.
/// </summary>
public sealed class CsvReportExportProfile : TabularReportExportProfile
{
    /// <summary>
    /// Gets or sets the field delimiter.
    /// </summary>
    public char Delimiter { get; set; } = ',';
}

/// <summary>
/// Configures XLSX export.
/// </summary>
public sealed class XlsxReportExportProfile : TabularReportExportProfile
{
    /// <summary>
    /// Gets or sets the workbook author metadata.
    /// </summary>
    public string WorkbookAuthor { get; set; } = "ProEdit";
}

/// <summary>
/// Represents one export request.
/// </summary>
public sealed class ReportExportRequest
{
    /// <summary>
    /// Gets or sets the execution result to export.
    /// </summary>
    public ReportExecutionResult ExecutionResult { get; set; } = new();

    /// <summary>
    /// Gets or sets the requested export format.
    /// </summary>
    public ReportExportFormat Format { get; set; }

    /// <summary>
    /// Gets or sets optional format-specific profile options.
    /// </summary>
    public ReportExportProfile? Profile { get; set; }
}

/// <summary>
/// Represents the outcome of one export operation.
/// </summary>
public sealed class ReportExportResult
{
    /// <summary>
    /// Gets or sets the exported media type.
    /// </summary>
    public string MediaType { get; set; } = "application/octet-stream";

    /// <summary>
    /// Gets or sets the recommended file extension.
    /// </summary>
    public string FileExtension { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of bytes written when known.
    /// </summary>
    public long? BytesWritten { get; set; }

    /// <summary>
    /// Gets the diagnostics emitted during export.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the export emitted any errors.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Exports reporting execution results into paginated or semantic output formats.
/// </summary>
public interface IReportExporter
{
    /// <summary>
    /// Exports the supplied execution result to the target stream.
    /// </summary>
    /// <param name="request">The export request.</param>
    /// <param name="stream">The target output stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export result.</returns>
    ValueTask<ReportExportResult> ExportAsync(
        ReportExportRequest request,
        Stream stream,
        CancellationToken cancellationToken = default);
}

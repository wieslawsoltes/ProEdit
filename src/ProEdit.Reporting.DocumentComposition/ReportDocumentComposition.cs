using ProEdit.Documents;

namespace ProEdit.Reporting.DocumentComposition;

/// <summary>
/// Represents one document-composition request.
/// </summary>
public sealed class ReportDocumentCompositionRequest
{
    /// <summary>
    /// Gets or sets the materialized report to compose.
    /// </summary>
    public MaterializedReport MaterializedReport { get; set; } = new();
}

/// <summary>
/// Represents one document-composition result.
/// </summary>
public sealed class ReportDocumentCompositionResult
{
    /// <summary>
    /// Gets or sets the composed document.
    /// </summary>
    public Document? Document { get; set; }

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets a value indicating whether composition emitted any errors.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Composes semantic reports into ProEdit documents.
/// </summary>
public interface IReportDocumentComposer
{
    /// <summary>
    /// Composes the supplied materialized report.
    /// </summary>
    /// <param name="request">The composition request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The composition result.</returns>
    ValueTask<ReportDocumentCompositionResult> ComposeAsync(
        ReportDocumentCompositionRequest request,
        CancellationToken cancellationToken = default);
}

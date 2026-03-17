namespace Vibe.Office.Reporting.Serialization;

/// <summary>
/// Result of reading a report template.
/// </summary>
public sealed class ReportTemplateReadResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportTemplateReadResult"/> class.
    /// </summary>
    /// <param name="reportDefinition">The parsed report definition, if available.</param>
    /// <param name="diagnostics">The emitted diagnostics.</param>
    public ReportTemplateReadResult(
        ReportDefinition? reportDefinition,
        IReadOnlyList<ReportDiagnostic> diagnostics)
    {
        ReportDefinition = reportDefinition;
        Diagnostics = diagnostics ?? Array.Empty<ReportDiagnostic>();
    }

    /// <summary>
    /// Gets the parsed report definition.
    /// </summary>
    public ReportDefinition? ReportDefinition { get; }

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public IReadOnlyList<ReportDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets a value indicating whether any error diagnostics were emitted.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Result of writing a report template.
/// </summary>
public sealed class ReportTemplateWriteResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportTemplateWriteResult"/> class.
    /// </summary>
    /// <param name="text">The written JSON payload.</param>
    /// <param name="diagnostics">The emitted diagnostics.</param>
    public ReportTemplateWriteResult(
        string text,
        IReadOnlyList<ReportDiagnostic> diagnostics)
    {
        Text = text;
        Diagnostics = diagnostics ?? Array.Empty<ReportDiagnostic>();
    }

    /// <summary>
    /// Gets the written JSON payload.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public IReadOnlyList<ReportDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets a value indicating whether any error diagnostics were emitted.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

namespace ProEdit.Reporting.Rdl;

/// <summary>
/// Result of reading an RDL payload.
/// </summary>
public sealed class ReportRdlReadResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportRdlReadResult"/> class.
    /// </summary>
    /// <param name="reportDefinition">The parsed report definition, if available.</param>
    /// <param name="diagnostics">The emitted diagnostics.</param>
    public ReportRdlReadResult(
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
/// Result of writing an RDL payload.
/// </summary>
public sealed class ReportRdlWriteResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportRdlWriteResult"/> class.
    /// </summary>
    /// <param name="xml">The written RDL payload.</param>
    /// <param name="diagnostics">The emitted diagnostics.</param>
    public ReportRdlWriteResult(
        string xml,
        IReadOnlyList<ReportDiagnostic> diagnostics)
    {
        Xml = xml;
        Diagnostics = diagnostics ?? Array.Empty<ReportDiagnostic>();
    }

    /// <summary>
    /// Gets the written RDL payload.
    /// </summary>
    public string Xml { get; }

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public IReadOnlyList<ReportDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets a value indicating whether any error diagnostics were emitted.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

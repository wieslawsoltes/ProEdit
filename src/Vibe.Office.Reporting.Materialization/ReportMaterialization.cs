using System.Globalization;
using Vibe.Office.Reporting.Data;

namespace Vibe.Office.Reporting.Materialization;

/// <summary>
/// Represents one materialization request.
/// </summary>
public sealed class ReportMaterializationRequest
{
    /// <summary>
    /// Gets or sets the report definition to materialize.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();

    /// <summary>
    /// Gets or sets the provider registry used for dataset execution.
    /// </summary>
    public ReportDataProviderRegistry ProviderRegistry { get; set; } = new();

    /// <summary>
    /// Gets or sets the host data registry used for in-memory and connector-backed data.
    /// </summary>
    public ReportHostDataRegistry HostDataRegistry { get; set; } = new();

    /// <summary>
    /// Gets the supplied parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ParameterValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the supplied global values.
    /// </summary>
    public Dictionary<string, object?> Globals { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the referenced subreport definitions.
    /// </summary>
    public Dictionary<string, ReportDefinition> ReferencedReports { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the execution culture.
    /// </summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Gets or sets the UI culture.
    /// </summary>
    public CultureInfo? UiCulture { get; set; }

    /// <summary>
    /// Gets or sets the execution time zone.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; set; }
}

/// <summary>
/// Represents one materialization result.
/// </summary>
public sealed class ReportMaterializationResult
{
    /// <summary>
    /// Gets or sets the semantic materialized report.
    /// </summary>
    public MaterializedReport? MaterializedReport { get; set; }

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets the resolved top-level parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ResolvedParameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether materialization emitted any errors.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Materializes report definitions into semantic execution results.
/// </summary>
public interface IReportMaterializer
{
    /// <summary>
    /// Materializes the supplied report.
    /// </summary>
    /// <param name="request">The materialization request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The materialization result.</returns>
    ValueTask<ReportMaterializationResult> MaterializeAsync(
        ReportMaterializationRequest request,
        CancellationToken cancellationToken = default);
}

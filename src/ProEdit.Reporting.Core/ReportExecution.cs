using System.Globalization;
using ProEdit.Documents;

namespace ProEdit.Reporting;

/// <summary>
/// Represents a parameter value supplied to or resolved by report execution.
/// </summary>
public sealed class ReportParameterValue
{
    /// <summary>
    /// Gets or sets a value indicating whether the parameter resolves to null.
    /// </summary>
    public bool IsNull { get; set; }

    /// <summary>
    /// Gets the typed values associated with the parameter.
    /// </summary>
    public List<object?> Values { get; set; } = new();

    /// <summary>
    /// Gets the display labels associated with the parameter values.
    /// </summary>
    public List<string> Labels { get; set; } = new();

    /// <summary>
    /// Returns the scalar parameter value when the parameter is not multi-value.
    /// </summary>
    /// <returns>The resolved scalar value or <see langword="null" />.</returns>
    public object? GetScalarValue()
    {
        if (IsNull || Values.Count == 0)
        {
            return null;
        }

        return Values[0];
    }

    /// <summary>
    /// Returns the scalar parameter label when one is available.
    /// </summary>
    /// <returns>The resolved display label or <see langword="null" />.</returns>
    public string? GetScalarLabel()
    {
        if (Labels.Count > 0)
        {
            return Labels[0];
        }

        return null;
    }

    /// <summary>
    /// Creates a parameter value from one scalar value.
    /// </summary>
    /// <param name="value">The scalar value.</param>
    /// <returns>The created parameter value.</returns>
    public static ReportParameterValue FromScalar(object? value)
    {
        var parameterValue = new ReportParameterValue
        {
            IsNull = value is null
        };

        if (value is not null)
        {
            parameterValue.Values.Add(value);
            parameterValue.Labels.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return parameterValue;
    }
}

/// <summary>
/// Represents one materialized dataset.
/// </summary>
public sealed class MaterializedDataSet
{
    /// <summary>
    /// Gets or sets the dataset identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets the materialized field definitions.
    /// </summary>
    public List<ReportFieldDefinition> Fields { get; } = new();

    /// <summary>
    /// Gets the materialized rows.
    /// </summary>
    public List<MaterializedDataRow> Rows { get; } = new();
}

/// <summary>
/// Represents one materialized row.
/// </summary>
public sealed class MaterializedDataRow
{
    /// <summary>
    /// Gets the row values by field name.
    /// </summary>
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents the semantic result of report execution before rendering.
/// </summary>
public sealed class MaterializedReport
{
    /// <summary>
    /// Gets or sets the report identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the report name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the report-level default font family.
    /// </summary>
    public string? DefaultFontFamily { get; set; }

    /// <summary>
    /// Gets or sets the generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets a value indicating whether whitespace in containers should be consumed when nested content grows.
    /// </summary>
    public bool ConsumeContainerWhitespace { get; set; }

    /// <summary>
    /// Gets the resolved parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ResolvedParameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the materialized datasets.
    /// </summary>
    public List<MaterializedDataSet> DataSets { get; } = new();

    /// <summary>
    /// Gets the semantic report sections produced during materialization.
    /// </summary>
    public List<MaterializedReportSection> Sections { get; } = new();

    /// <summary>
    /// Gets execution diagnostics tied to the materialized result.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();
}

/// <summary>
/// Defines report execution options.
/// </summary>
public sealed class ReportExecutionRequest
{
    /// <summary>
    /// Gets or sets the report definition to execute.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();

    /// <summary>
    /// Gets the supplied parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ParameterValues { get; } = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Gets or sets the execution mode.
    /// </summary>
    public ReportExecutionMode ExecutionMode { get; set; } = ReportExecutionMode.Default;

    /// <summary>
    /// Gets or sets the cache policy.
    /// </summary>
    public ReportCachePolicy CachePolicy { get; set; } = ReportCachePolicy.PreferCache;
}

/// <summary>
/// Supported execution modes.
/// </summary>
public enum ReportExecutionMode
{
    /// <summary>
    /// Default execution.
    /// </summary>
    Default,

    /// <summary>
    /// Preview execution.
    /// </summary>
    Preview,

    /// <summary>
    /// Export execution.
    /// </summary>
    Export
}

/// <summary>
/// Supported cache policies.
/// </summary>
public enum ReportCachePolicy
{
    /// <summary>
    /// Prefer a cached result when available.
    /// </summary>
    PreferCache,

    /// <summary>
    /// Force a fresh execution.
    /// </summary>
    BypassCache
}

/// <summary>
/// Captures execution metrics.
/// </summary>
public sealed class ReportExecutionMetrics
{
    /// <summary>
    /// Gets or sets the execution duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the rendered page count.
    /// </summary>
    public int PageCount { get; set; }

    /// <summary>
    /// Gets or sets the materialized row count.
    /// </summary>
    public int DataRowCount { get; set; }
}

/// <summary>
/// Represents the result of report execution.
/// </summary>
public sealed class ReportExecutionResult
{
    /// <summary>
    /// Gets or sets the semantic execution result.
    /// </summary>
    public MaterializedReport? MaterializedReport { get; set; }

    /// <summary>
    /// Gets or sets the rendered document.
    /// </summary>
    public Document? Document { get; set; }

    /// <summary>
    /// Gets the diagnostics emitted during execution.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets the execution metrics.
    /// </summary>
    public ReportExecutionMetrics Metrics { get; } = new();

    /// <summary>
    /// Gets the resolved parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ResolvedParameters { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Executes reports into semantic and rendered results.
/// </summary>
public interface IReportExecutor
{
    /// <summary>
    /// Executes the supplied report request.
    /// </summary>
    /// <param name="request">The report execution request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The execution result.</returns>
    ValueTask<ReportExecutionResult> ExecuteAsync(
        ReportExecutionRequest request,
        CancellationToken cancellationToken = default);
}

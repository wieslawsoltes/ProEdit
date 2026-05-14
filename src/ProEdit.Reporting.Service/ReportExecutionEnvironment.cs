using ProEdit.Reporting.Data;

namespace ProEdit.Reporting.Service;

/// <summary>
/// Captures the host-side runtime state used by report execution.
/// </summary>
public sealed class ReportExecutionEnvironment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportExecutionEnvironment" /> class.
    /// </summary>
    public ReportExecutionEnvironment()
        : this(ReportDataProviders.CreateDefaultRegistry(), new ReportHostDataRegistry())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportExecutionEnvironment" /> class.
    /// </summary>
    /// <param name="providerRegistry">The provider registry.</param>
    /// <param name="hostDataRegistry">The host data registry.</param>
    public ReportExecutionEnvironment(
        ReportDataProviderRegistry providerRegistry,
        ReportHostDataRegistry hostDataRegistry)
    {
        ProviderRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        HostDataRegistry = hostDataRegistry ?? throw new ArgumentNullException(nameof(hostDataRegistry));
    }

    /// <summary>
    /// Gets the data provider registry used for dataset execution.
    /// </summary>
    public ReportDataProviderRegistry ProviderRegistry { get; }

    /// <summary>
    /// Gets the host data registry used by built-in providers.
    /// </summary>
    public ReportHostDataRegistry HostDataRegistry { get; }

    /// <summary>
    /// Gets the referenced subreport definitions made available to the runtime.
    /// </summary>
    public Dictionary<string, ReportDefinition> ReferencedReports { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the additional global values made available to expressions.
    /// </summary>
    public Dictionary<string, object?> Globals { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a default execution environment with built-in providers.
    /// </summary>
    /// <returns>The created environment.</returns>
    public static ReportExecutionEnvironment CreateDefault()
    {
        return new ReportExecutionEnvironment();
    }
}

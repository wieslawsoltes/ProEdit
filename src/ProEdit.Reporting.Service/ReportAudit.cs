namespace ProEdit.Reporting.Service;

/// <summary>
/// Describes the service-layer audit event category.
/// </summary>
public enum ReportAuditEventKind
{
    /// <summary>
    /// A report execution completed.
    /// </summary>
    ExecutionCompleted,

    /// <summary>
    /// A report delivery attempt completed.
    /// </summary>
    DeliveryCompleted,

    /// <summary>
    /// A schedule run completed.
    /// </summary>
    ScheduleCompleted
}

/// <summary>
/// Represents one audit entry emitted by the optional service layer.
/// </summary>
public sealed class ReportAuditEntry
{
    /// <summary>
    /// Gets or sets the audit entry identifier.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the UTC timestamp for the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the event kind.
    /// </summary>
    public ReportAuditEventKind EventKind { get; set; }

    /// <summary>
    /// Gets or sets the event severity.
    /// </summary>
    public ReportDiagnosticSeverity Severity { get; set; } = ReportDiagnosticSeverity.Info;

    /// <summary>
    /// Gets or sets the audit message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the report identifier when applicable.
    /// </summary>
    public string? ReportId { get; set; }

    /// <summary>
    /// Gets or sets the report revision number when applicable.
    /// </summary>
    public int? RevisionNumber { get; set; }

    /// <summary>
    /// Gets or sets the schedule identifier when applicable.
    /// </summary>
    public string? ScheduleId { get; set; }

    /// <summary>
    /// Gets or sets the delivery target identifier when applicable.
    /// </summary>
    public string? DeliveryTargetId { get; set; }

    /// <summary>
    /// Gets the additional audit metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Stores service-layer audit entries.
/// </summary>
public interface IReportAuditLog
{
    /// <summary>
    /// Writes one audit entry.
    /// </summary>
    /// <param name="entry">The audit entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completion task.</returns>
    ValueTask WriteAsync(
        ReportAuditEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all stored audit entries.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored entries.</returns>
    ValueTask<IReadOnlyList<ReportAuditEntry>> ListAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory implementation of <see cref="IReportAuditLog" />.
/// </summary>
public sealed class InMemoryReportAuditLog : IReportAuditLog
{
    private readonly object _gate = new();
    private readonly List<ReportAuditEntry> _entries = new();

    /// <inheritdoc />
    public ValueTask WriteAsync(
        ReportAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _entries.Add(ReportServiceModelCloner.CloneAuditEntry(entry));
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ReportAuditEntry>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var items = new ReportAuditEntry[_entries.Count];
            for (var index = 0; index < _entries.Count; index++)
            {
                items[index] = ReportServiceModelCloner.CloneAuditEntry(_entries[index]);
            }

            return ValueTask.FromResult<IReadOnlyList<ReportAuditEntry>>(items);
        }
    }
}

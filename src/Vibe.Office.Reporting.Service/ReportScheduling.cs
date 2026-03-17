using Vibe.Office.Reporting.Export;

namespace Vibe.Office.Reporting.Service;

/// <summary>
/// Defines the export configuration used by service-layer delivery and schedules.
/// </summary>
public sealed class ReportScheduledOutputDefinition
{
    /// <summary>
    /// Gets or sets the export format.
    /// </summary>
    public ReportExportFormat Format { get; set; } = ReportExportFormat.Pdf;

    /// <summary>
    /// Gets or sets the optional file name pattern.
    /// </summary>
    public string? FileNamePattern { get; set; }

    /// <summary>
    /// Gets or sets the optional tablix identifier for CSV and XLSX exports.
    /// </summary>
    public string? TablixItemId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether header rows should be included for tabular exports.
    /// </summary>
    public bool IncludeHeaderRows { get; set; } = true;

    /// <summary>
    /// Gets or sets the CSV delimiter.
    /// </summary>
    public char CsvDelimiter { get; set; } = ',';

    /// <summary>
    /// Gets or sets the XLSX workbook author.
    /// </summary>
    public string WorkbookAuthor { get; set; } = "VibeOffice";
}

/// <summary>
/// Defines one persisted report schedule.
/// </summary>
public sealed class ReportScheduleDefinition
{
    /// <summary>
    /// Gets or sets the schedule identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the schedule name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the report identifier.
    /// </summary>
    public string ReportId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional report revision number. When <see langword="null" />, the latest revision is used.
    /// </summary>
    public int? RevisionNumber { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the schedule is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the first eligible run timestamp in UTC.
    /// </summary>
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the optional inclusive end timestamp in UTC.
    /// </summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>
    /// Gets or sets the execution interval.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the next scheduled run timestamp in UTC.
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>
    /// Gets or sets the last completed run timestamp in UTC.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// Gets the supplied schedule parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ParameterValues { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the delivery target identifiers.
    /// </summary>
    public List<string> DeliveryTargetIds { get; } = new();

    /// <summary>
    /// Gets or sets the output definition.
    /// </summary>
    public ReportScheduledOutputDefinition Output { get; set; } = new();

    /// <summary>
    /// Gets the arbitrary schedule metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents one due schedule snapshot.
/// </summary>
public sealed class ReportScheduleDueWork
{
    /// <summary>
    /// Gets or sets the schedule identifier.
    /// </summary>
    public string ScheduleId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the due timestamp in UTC.
    /// </summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>
    /// Gets or sets the schedule snapshot.
    /// </summary>
    public ReportScheduleDefinition Schedule { get; set; } = new();
}

/// <summary>
/// Stores schedule definitions and due-work state.
/// </summary>
public interface IReportScheduleRepository
{
    /// <summary>
    /// Saves one schedule definition.
    /// </summary>
    /// <param name="schedule">The schedule.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completion task.</returns>
    ValueTask SaveAsync(
        ReportScheduleDefinition schedule,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one schedule definition.
    /// </summary>
    /// <param name="scheduleId">The schedule identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The schedule or <see langword="null" />.</returns>
    ValueTask<ReportScheduleDefinition?> GetAsync(
        string scheduleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all schedule definitions.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The schedules.</returns>
    ValueTask<IReadOnlyList<ReportScheduleDefinition>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all schedules due on or before the supplied UTC timestamp.
    /// </summary>
    /// <param name="dueBeforeUtc">The due cutoff in UTC.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The due schedules.</returns>
    ValueTask<IReadOnlyList<ReportScheduleDueWork>> ListDueAsync(
        DateTimeOffset dueBeforeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates schedule execution state after a run.
    /// </summary>
    /// <param name="scheduleId">The schedule identifier.</param>
    /// <param name="executedAtUtc">The execution timestamp in UTC.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the schedule exists; otherwise <see langword="false" />.</returns>
    ValueTask<bool> UpdateExecutionAsync(
        string scheduleId,
        DateTimeOffset executedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory implementation of <see cref="IReportScheduleRepository" />.
/// </summary>
public sealed class InMemoryReportScheduleRepository : IReportScheduleRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ReportScheduleDefinition> _schedules =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask SaveAsync(
        ReportScheduleDefinition schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.ReportId);
        cancellationToken.ThrowIfCancellationRequested();

        if (schedule.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(schedule), "Schedules must define a positive interval.");
        }

        var clone = ReportServiceModelCloner.CloneSchedule(schedule);
        if (!clone.NextRunAt.HasValue && !clone.LastRunAt.HasValue)
        {
            clone.NextRunAt = clone.StartsAt;
        }

        if (clone.EndsAt.HasValue
            && clone.NextRunAt.HasValue
            && clone.NextRunAt.Value > clone.EndsAt.Value)
        {
            clone.NextRunAt = null;
        }

        lock (_gate)
        {
            _schedules[clone.Id] = clone;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<ReportScheduleDefinition?> GetAsync(
        string scheduleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _schedules.TryGetValue(scheduleId, out var schedule)
                    ? ReportServiceModelCloner.CloneSchedule(schedule)
                    : null);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ReportScheduleDefinition>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var items = new ReportScheduleDefinition[_schedules.Count];
            var index = 0;
            foreach (var pair in _schedules.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                items[index++] = ReportServiceModelCloner.CloneSchedule(pair.Value);
            }

            return ValueTask.FromResult<IReadOnlyList<ReportScheduleDefinition>>(items);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ReportScheduleDueWork>> ListDueAsync(
        DateTimeOffset dueBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var due = new List<ReportScheduleDueWork>();
            foreach (var pair in _schedules)
            {
                if (!TryGetDueAt(pair.Value, dueBeforeUtc, out var dueAt))
                {
                    continue;
                }

                due.Add(new ReportScheduleDueWork
                {
                    ScheduleId = pair.Key,
                    DueAt = dueAt,
                    Schedule = ReportServiceModelCloner.CloneSchedule(pair.Value)
                });
            }

            due.Sort(static (left, right) => left.DueAt.CompareTo(right.DueAt));
            return ValueTask.FromResult<IReadOnlyList<ReportScheduleDueWork>>(due);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> UpdateExecutionAsync(
        string scheduleId,
        DateTimeOffset executedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_schedules.TryGetValue(scheduleId, out var schedule))
            {
                return ValueTask.FromResult(false);
            }

            schedule.LastRunAt = executedAtUtc;
            schedule.NextRunAt = CalculateNextRunAt(schedule, executedAtUtc);
            return ValueTask.FromResult(true);
        }
    }

    private static bool TryGetDueAt(
        ReportScheduleDefinition schedule,
        DateTimeOffset dueBeforeUtc,
        out DateTimeOffset dueAt)
    {
        if (!schedule.IsEnabled)
        {
            dueAt = default;
            return false;
        }

        if (!schedule.NextRunAt.HasValue)
        {
            dueAt = default;
            return false;
        }

        dueAt = schedule.NextRunAt.Value;
        if (schedule.EndsAt.HasValue && dueAt > schedule.EndsAt.Value)
        {
            return false;
        }

        return dueAt <= dueBeforeUtc;
    }

    private static DateTimeOffset? CalculateNextRunAt(
        ReportScheduleDefinition schedule,
        DateTimeOffset executedAtUtc)
    {
        var nextRunAt = schedule.NextRunAt ?? schedule.StartsAt;
        if (nextRunAt > executedAtUtc)
        {
            return schedule.EndsAt.HasValue && nextRunAt > schedule.EndsAt.Value
                ? null
                : nextRunAt;
        }

        do
        {
            nextRunAt = nextRunAt.Add(schedule.Interval);
        }
        while (nextRunAt <= executedAtUtc);

        return schedule.EndsAt.HasValue && nextRunAt > schedule.EndsAt.Value
            ? null
            : nextRunAt;
    }
}

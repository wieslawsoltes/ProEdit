using Vibe.Office.Reporting.Serialization;

namespace Vibe.Office.Reporting.Service;

/// <summary>
/// Represents one repository save request.
/// </summary>
public sealed class ReportRepositorySaveRequest
{
    /// <summary>
    /// Gets or sets the report definition to store.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();

    /// <summary>
    /// Gets or sets the optional revision comment.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Gets or sets the optional actor.
    /// </summary>
    public string? Actor { get; set; }
}

/// <summary>
/// Represents one immutable repository revision snapshot.
/// </summary>
public sealed class ReportRepositoryRevision
{
    /// <summary>
    /// Gets or sets the report identifier.
    /// </summary>
    public string ReportId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the revision number.
    /// </summary>
    public int RevisionNumber { get; set; }

    /// <summary>
    /// Gets or sets the storage timestamp.
    /// </summary>
    public DateTimeOffset StoredAt { get; set; }

    /// <summary>
    /// Gets or sets the optional revision comment.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Gets or sets the optional actor.
    /// </summary>
    public string? Actor { get; set; }

    /// <summary>
    /// Gets or sets the report definition snapshot.
    /// </summary>
    public ReportDefinition ReportDefinition { get; set; } = new();
}

/// <summary>
/// Represents one repository list item.
/// </summary>
public sealed class ReportRepositoryListItem
{
    /// <summary>
    /// Gets or sets the report identifier.
    /// </summary>
    public string ReportId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name from the latest revision.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description from the latest revision.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the latest revision number.
    /// </summary>
    public int LatestRevisionNumber { get; set; }

    /// <summary>
    /// Gets or sets the latest modification timestamp.
    /// </summary>
    public DateTimeOffset LatestStoredAt { get; set; }
}

/// <summary>
/// Represents the outcome of one repository save.
/// </summary>
public sealed class ReportRepositorySaveResult
{
    /// <summary>
    /// Gets or sets the stored revision snapshot when successful.
    /// </summary>
    public ReportRepositoryRevision? Revision { get; set; }

    /// <summary>
    /// Gets the emitted diagnostics.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets a value indicating whether any error diagnostics were emitted.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Stores versioned report definitions.
/// </summary>
public interface IReportRepository
{
    /// <summary>
    /// Saves one new revision.
    /// </summary>
    /// <param name="request">The save request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The save result.</returns>
    ValueTask<ReportRepositorySaveResult> SaveAsync(
        ReportRepositorySaveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one revision snapshot.
    /// </summary>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="revisionNumber">The optional revision number. When <see langword="null" />, the latest revision is returned.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The revision snapshot or <see langword="null" />.</returns>
    ValueTask<ReportRepositoryRevision?> GetRevisionAsync(
        string reportId,
        int? revisionNumber = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the latest snapshot metadata for all stored reports.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list items.</returns>
    ValueTask<IReadOnlyList<ReportRepositoryListItem>> ListAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory implementation of <see cref="IReportRepository" />.
/// </summary>
public sealed class InMemoryReportRepository : IReportRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<StoredRevision>> _reports =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IReportClock _clock;
    private readonly IReportTemplateSerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryReportRepository" /> class.
    /// </summary>
    /// <param name="serializer">The template serializer used for cloning snapshots.</param>
    /// <param name="clock">The clock.</param>
    public InMemoryReportRepository(
        IReportTemplateSerializer? serializer = null,
        IReportClock? clock = null)
    {
        _serializer = serializer ?? new ReportTemplateSerializer();
        _clock = clock ?? new SystemReportClock();
    }

    /// <inheritdoc />
    public ValueTask<ReportRepositorySaveResult> SaveAsync(
        ReportRepositorySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ReportDefinition);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new ReportRepositorySaveResult();
        if (string.IsNullOrWhiteSpace(request.ReportDefinition.Id))
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.InvalidTemplate,
                "Stored report definitions must define a non-empty identifier.",
                "$.id"));
            return ValueTask.FromResult(result);
        }

        var clonedDefinition = ReportServiceModelCloner.CloneReportDefinition(
            _serializer,
            request.ReportDefinition,
            result.Diagnostics);
        if (clonedDefinition is null)
        {
            return ValueTask.FromResult(result);
        }

        StoredRevision storedRevision;
        lock (_gate)
        {
            if (!_reports.TryGetValue(clonedDefinition.Id, out var revisions))
            {
                revisions = new List<StoredRevision>();
                _reports[clonedDefinition.Id] = revisions;
            }

            storedRevision = new StoredRevision(
                revisions.Count + 1,
                _clock.UtcNow,
                request.Comment,
                request.Actor,
                clonedDefinition);
            revisions.Add(storedRevision);
        }

        result.Revision = CloneRevision(storedRevision);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public ValueTask<ReportRepositoryRevision?> GetRevisionAsync(
        string reportId,
        int? revisionNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_reports.TryGetValue(reportId, out var revisions) || revisions.Count == 0)
            {
                return ValueTask.FromResult<ReportRepositoryRevision?>(null);
            }

            StoredRevision? stored = null;
            if (revisionNumber.HasValue)
            {
                var index = revisionNumber.Value - 1;
                if (index >= 0 && index < revisions.Count)
                {
                    stored = revisions[index];
                }
            }
            else
            {
                stored = revisions[^1];
            }

            return ValueTask.FromResult(stored is null ? null : CloneRevision(stored));
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ReportRepositoryListItem>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var items = new List<ReportRepositoryListItem>(_reports.Count);
            foreach (var pair in _reports)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                var latest = pair.Value[^1];
                items.Add(new ReportRepositoryListItem
                {
                    ReportId = pair.Key,
                    Name = latest.ReportDefinition.Name,
                    Description = latest.ReportDefinition.Description,
                    LatestRevisionNumber = latest.RevisionNumber,
                    LatestStoredAt = latest.StoredAt
                });
            }

            items.Sort(static (left, right) => string.Compare(left.ReportId, right.ReportId, StringComparison.OrdinalIgnoreCase));
            return ValueTask.FromResult<IReadOnlyList<ReportRepositoryListItem>>(items);
        }
    }

    private ReportRepositoryRevision CloneRevision(StoredRevision revision)
    {
        var diagnostics = new List<ReportDiagnostic>();
        var clonedDefinition = ReportServiceModelCloner.CloneReportDefinition(
            _serializer,
            revision.ReportDefinition,
            diagnostics);
        if (clonedDefinition is null)
        {
            throw new InvalidOperationException("Stored report revision could not be cloned.");
        }

        return new ReportRepositoryRevision
        {
            ReportId = clonedDefinition.Id,
            RevisionNumber = revision.RevisionNumber,
            StoredAt = revision.StoredAt,
            Comment = revision.Comment,
            Actor = revision.Actor,
            ReportDefinition = clonedDefinition
        };
    }

    private sealed record StoredRevision(
        int RevisionNumber,
        DateTimeOffset StoredAt,
        string? Comment,
        string? Actor,
        ReportDefinition ReportDefinition);
}

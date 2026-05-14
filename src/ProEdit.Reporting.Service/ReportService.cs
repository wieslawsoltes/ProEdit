using System.Globalization;
using ProEdit.Reporting.Export;

namespace ProEdit.Reporting.Service;

/// <summary>
/// Represents one service-layer execution request.
/// </summary>
public sealed class ReportServiceExecutionRequest
{
    /// <summary>
    /// Gets or sets the repository report identifier.
    /// </summary>
    public string ReportId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional revision number. When <see langword="null" />, the latest revision is executed.
    /// </summary>
    public int? RevisionNumber { get; set; }

    /// <summary>
    /// Gets the supplied parameter values.
    /// </summary>
    public Dictionary<string, ReportParameterValue> ParameterValues { get; } =
        new(StringComparer.OrdinalIgnoreCase);

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
    /// Gets or sets the optional host-side execution environment.
    /// </summary>
    public ReportExecutionEnvironment? Environment { get; set; }

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
/// Represents the outcome of one service-layer execution.
/// </summary>
public sealed class ReportServiceExecutionResult
{
    /// <summary>
    /// Gets or sets the resolved repository revision.
    /// </summary>
    public ReportRepositoryRevision? Revision { get; set; }

    /// <summary>
    /// Gets the reporting execution result.
    /// </summary>
    public ReportExecutionResult ExecutionResult { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the operation emitted any errors.
    /// </summary>
    public bool HasErrors => ExecutionResult.Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Represents one service-layer execute-and-deliver request.
/// </summary>
public sealed class ReportServiceDeliveryRequest
{
    /// <summary>
    /// Gets or sets the execution request.
    /// </summary>
    public ReportServiceExecutionRequest ExecutionRequest { get; set; } = new();

    /// <summary>
    /// Gets or sets the output definition.
    /// </summary>
    public ReportScheduledOutputDefinition Output { get; set; } = new();

    /// <summary>
    /// Gets the delivery target identifiers.
    /// </summary>
    public List<string> DeliveryTargetIds { get; } = new();

    /// <summary>
    /// Gets or sets the optional schedule identifier.
    /// </summary>
    public string? ScheduleId { get; set; }
}

/// <summary>
/// Represents the outcome of one service-layer delivery operation.
/// </summary>
public sealed class ReportServiceDeliveryResult
{
    /// <summary>
    /// Gets or sets the execution result.
    /// </summary>
    public ReportServiceExecutionResult Execution { get; set; } = new();

    /// <summary>
    /// Gets the emitted service-layer diagnostics.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets the delivery results.
    /// </summary>
    public List<ReportDeliveryResult> DeliveryResults { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the operation emitted any errors.
    /// </summary>
    public bool HasErrors =>
        Execution.HasErrors
        || Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error)
        || DeliveryResults.Any(static result => result.HasErrors);
}

/// <summary>
/// Represents the outcome of one schedule execution.
/// </summary>
public sealed class ReportScheduledRunResult
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
    /// Gets or sets the execution timestamp in UTC.
    /// </summary>
    public DateTimeOffset ExecutedAt { get; set; }

    /// <summary>
    /// Gets or sets the delivery result.
    /// </summary>
    public ReportServiceDeliveryResult Delivery { get; set; } = new();

    /// <summary>
    /// Gets the emitted schedule diagnostics.
    /// </summary>
    public List<ReportDiagnostic> Diagnostics { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the operation emitted any errors.
    /// </summary>
    public bool HasErrors =>
        Delivery.HasErrors
        || Diagnostics.Any(static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
}

/// <summary>
/// Orchestrates repository lookup, execution, export, delivery, scheduling, and audit logging.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Executes one stored report.
    /// </summary>
    /// <param name="request">The execution request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The execution result.</returns>
    ValueTask<ReportServiceExecutionResult> ExecuteAsync(
        ReportServiceExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes one stored report and delivers the exported result.
    /// </summary>
    /// <param name="request">The delivery request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The delivery result.</returns>
    ValueTask<ReportServiceDeliveryResult> ExecuteAndDeliverAsync(
        ReportServiceDeliveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes one stored schedule immediately.
    /// </summary>
    /// <param name="scheduleId">The schedule identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The schedule run result.</returns>
    ValueTask<ReportScheduledRunResult> RunScheduleAsync(
        string scheduleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes all schedules due on or before the supplied UTC timestamp.
    /// </summary>
    /// <param name="dueBeforeUtc">The optional due cutoff. When <see langword="null" />, the service clock is used.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The schedule run results.</returns>
    ValueTask<IReadOnlyList<ReportScheduledRunResult>> RunDueSchedulesAsync(
        DateTimeOffset? dueBeforeUtc = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of <see cref="IReportService" />.
/// </summary>
public sealed class ReportService : IReportService
{
    private readonly IReportAuditLog _auditLog;
    private readonly IReportClock _clock;
    private readonly ReportDeliveryChannelRegistry _deliveryChannels;
    private readonly IReportDeliveryTargetRepository _deliveryTargets;
    private readonly IReportExporter _exporter;
    private readonly IReportRepository _reportRepository;
    private readonly IReportScheduleRepository _scheduleRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportService" /> class.
    /// </summary>
    /// <param name="reportRepository">The report repository.</param>
    /// <param name="scheduleRepository">The optional schedule repository.</param>
    /// <param name="deliveryTargetRepository">The optional delivery target repository.</param>
    /// <param name="deliveryChannels">The optional delivery channel registry.</param>
    /// <param name="auditLog">The optional audit log.</param>
    /// <param name="clock">The optional clock.</param>
    /// <param name="exporter">The optional exporter.</param>
    public ReportService(
        IReportRepository reportRepository,
        IReportScheduleRepository? scheduleRepository = null,
        IReportDeliveryTargetRepository? deliveryTargetRepository = null,
        ReportDeliveryChannelRegistry? deliveryChannels = null,
        IReportAuditLog? auditLog = null,
        IReportClock? clock = null,
        IReportExporter? exporter = null)
    {
        _reportRepository = reportRepository ?? throw new ArgumentNullException(nameof(reportRepository));
        _scheduleRepository = scheduleRepository ?? new InMemoryReportScheduleRepository();
        _deliveryTargets = deliveryTargetRepository ?? new InMemoryReportDeliveryTargetRepository();
        _deliveryChannels = deliveryChannels ?? ReportDeliveryChannelRegistry.CreateDefault();
        _auditLog = auditLog ?? new InMemoryReportAuditLog();
        _clock = clock ?? new SystemReportClock();
        _exporter = exporter ?? new ReportExporter();
    }

    /// <inheritdoc />
    public async ValueTask<ReportServiceExecutionResult> ExecuteAsync(
        ReportServiceExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ReportServiceExecutionResult();
        if (string.IsNullOrWhiteSpace(request.ReportId))
        {
            result.ExecutionResult.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.RepositoryItemNotFound,
                "A non-empty report identifier is required.",
                "$.reportId"));
            return result;
        }

        var revision = await _reportRepository.GetRevisionAsync(
            request.ReportId,
            request.RevisionNumber,
            cancellationToken);
        if (revision is null)
        {
            result.ExecutionResult.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.RepositoryItemNotFound,
                $"Report '{request.ReportId}' was not found in the repository.",
                "$.reportId"));
            await WriteAuditEntryAsync(
                ReportAuditEventKind.ExecutionCompleted,
                ReportDiagnosticSeverity.Error,
                $"Execution failed because report '{request.ReportId}' was not found.",
                request.ReportId,
                request.RevisionNumber,
                scheduleId: null,
                deliveryTargetId: null,
                cancellationToken);
            return result;
        }

        result.Revision = revision;

        var environment = await BuildExecutionEnvironmentAsync(
            revision.ReportDefinition,
            request.Environment,
            cancellationToken);
        var executor = new ReportExecutor(environment);

        var executionRequest = new ReportExecutionRequest
        {
            ReportDefinition = revision.ReportDefinition,
            Culture = request.Culture,
            UiCulture = request.UiCulture,
            TimeZone = request.TimeZone,
            ExecutionMode = request.ExecutionMode,
            CachePolicy = request.CachePolicy
        };
        ReportServiceModelCloner.CopyParameters(request.ParameterValues, executionRequest.ParameterValues);

        var executionResult = await executor.ExecuteAsync(executionRequest, cancellationToken);
        result.ExecutionResult.MaterializedReport = executionResult.MaterializedReport;
        result.ExecutionResult.Document = executionResult.Document;
        result.ExecutionResult.Metrics.Duration = executionResult.Metrics.Duration;
        result.ExecutionResult.Metrics.DataRowCount = executionResult.Metrics.DataRowCount;
        result.ExecutionResult.Metrics.PageCount = executionResult.Metrics.PageCount;
        ReportServiceModelCloner.AddDiagnostics(executionResult.Diagnostics, result.ExecutionResult.Diagnostics);
        ReportServiceModelCloner.CopyParameters(executionResult.ResolvedParameters, result.ExecutionResult.ResolvedParameters);

        await WriteAuditEntryAsync(
            ReportAuditEventKind.ExecutionCompleted,
            result.HasErrors ? ReportDiagnosticSeverity.Error : ReportDiagnosticSeverity.Info,
            result.HasErrors
                ? $"Execution completed with errors for report '{revision.ReportId}'."
                : $"Execution completed for report '{revision.ReportId}'.",
            revision.ReportId,
            revision.RevisionNumber,
            scheduleId: null,
            deliveryTargetId: null,
            cancellationToken);

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<ReportServiceDeliveryResult> ExecuteAndDeliverAsync(
        ReportServiceDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExecutionRequest);
        ArgumentNullException.ThrowIfNull(request.Output);

        var result = new ReportServiceDeliveryResult
        {
            Execution = await ExecuteAsync(request.ExecutionRequest, cancellationToken)
        };

        if (result.Execution.HasErrors || result.Execution.Revision is null)
        {
            return result;
        }

        var exportRequest = CreateExportRequest(result.Execution.ExecutionResult, request.Output);
        using var stream = new MemoryStream();
        ReportExportResult exportResult;
        try
        {
            exportResult = await _exporter.ExportAsync(exportRequest, stream, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.ExportFailed,
                ex.Message,
                "$.output.format"));
            return result;
        }

        ReportServiceModelCloner.AddDiagnostics(exportResult.Diagnostics, result.Diagnostics);
        if (exportResult.HasErrors)
        {
            return result;
        }

        var timestamp = _clock.UtcNow;
        var payload = new ReportDeliveryPayload
        {
            ReportId = result.Execution.Revision.ReportId,
            ReportName = result.Execution.Revision.ReportDefinition.Name,
            ScheduleId = request.ScheduleId,
            FileExtension = exportResult.FileExtension,
            MediaType = exportResult.MediaType,
            FileName = ResolveOutputFileName(
                request.Output,
                result.Execution.Revision.ReportId,
                result.Execution.Revision.ReportDefinition.Name,
                request.ScheduleId,
                timestamp,
                exportResult.FileExtension),
            Content = stream.ToArray(),
            CreatedAt = timestamp
        };

        for (var targetIndex = 0; targetIndex < request.DeliveryTargetIds.Count; targetIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetId = request.DeliveryTargetIds[targetIndex];
            var target = await _deliveryTargets.GetAsync(targetId, cancellationToken);
            if (target is null)
            {
                var diagnostic = new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.DeliveryTargetNotFound,
                    $"Delivery target '{targetId}' was not found.",
                    $"$.deliveryTargetIds[{targetIndex}]");
                result.Diagnostics.Add(diagnostic);
                await WriteAuditEntryAsync(
                    ReportAuditEventKind.DeliveryCompleted,
                    ReportDiagnosticSeverity.Error,
                    diagnostic.Message,
                    result.Execution.Revision.ReportId,
                    result.Execution.Revision.RevisionNumber,
                    request.ScheduleId,
                    targetId,
                    cancellationToken);
                continue;
            }

            if (!target.IsEnabled)
            {
                continue;
            }

            if (!_deliveryChannels.TryGetChannel(target.ChannelId, out var channel))
            {
                var diagnostic = new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.DeliveryChannelNotFound,
                    $"Delivery channel '{target.ChannelId}' was not found.",
                    $"$.deliveryTargetIds[{targetIndex}]");
                result.Diagnostics.Add(diagnostic);
                await WriteAuditEntryAsync(
                    ReportAuditEventKind.DeliveryCompleted,
                    ReportDiagnosticSeverity.Error,
                    diagnostic.Message,
                    result.Execution.Revision.ReportId,
                    result.Execution.Revision.RevisionNumber,
                    request.ScheduleId,
                    target.Id,
                    cancellationToken);
                continue;
            }

            ReportDeliveryResult deliveryResult;
            try
            {
                deliveryResult = await channel.DeliverAsync(target, payload, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                deliveryResult = new ReportDeliveryResult
                {
                    TargetId = target.Id,
                    ChannelId = channel.ChannelId
                };
                deliveryResult.Diagnostics.Add(new ReportDiagnostic(
                    ReportDiagnosticSeverity.Error,
                    ReportDiagnosticCodes.DeliveryFailed,
                    ex.Message,
                    $"$.deliveryTargetIds[{targetIndex}]"));
            }

            result.DeliveryResults.Add(deliveryResult);
            await WriteAuditEntryAsync(
                ReportAuditEventKind.DeliveryCompleted,
                deliveryResult.HasErrors ? ReportDiagnosticSeverity.Error : ReportDiagnosticSeverity.Info,
                deliveryResult.HasErrors
                    ? $"Delivery to target '{target.Id}' completed with errors."
                    : $"Delivery to target '{target.Id}' completed.",
                result.Execution.Revision.ReportId,
                result.Execution.Revision.RevisionNumber,
                request.ScheduleId,
                target.Id,
                cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<ReportScheduledRunResult> RunScheduleAsync(
        string scheduleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);

        var schedule = await _scheduleRepository.GetAsync(scheduleId, cancellationToken);
        if (schedule is null)
        {
            return new ReportScheduledRunResult
            {
                ScheduleId = scheduleId,
                ExecutedAt = _clock.UtcNow,
                Diagnostics =
                {
                    new ReportDiagnostic(
                        ReportDiagnosticSeverity.Error,
                        ReportDiagnosticCodes.ScheduleNotFound,
                        $"Schedule '{scheduleId}' was not found.",
                        "$.scheduleId")
                }
            };
        }

        var dueAt = schedule.NextRunAt ?? schedule.StartsAt;
        return await RunScheduleCoreAsync(schedule, dueAt, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ReportScheduledRunResult>> RunDueSchedulesAsync(
        DateTimeOffset? dueBeforeUtc = null,
        CancellationToken cancellationToken = default)
    {
        var cutoff = dueBeforeUtc ?? _clock.UtcNow;
        var dueSchedules = await _scheduleRepository.ListDueAsync(cutoff, cancellationToken);
        var results = new ReportScheduledRunResult[dueSchedules.Count];

        for (var index = 0; index < dueSchedules.Count; index++)
        {
            results[index] = await RunScheduleCoreAsync(
                dueSchedules[index].Schedule,
                dueSchedules[index].DueAt,
                cancellationToken);
        }

        return results;
    }

    private async ValueTask<ReportScheduledRunResult> RunScheduleCoreAsync(
        ReportScheduleDefinition schedule,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var executedAt = _clock.UtcNow;
        var result = new ReportScheduledRunResult
        {
            ScheduleId = schedule.Id,
            DueAt = dueAt,
            ExecutedAt = executedAt
        };

        var deliveryRequest = new ReportServiceDeliveryRequest
        {
            ExecutionRequest = new ReportServiceExecutionRequest
            {
                ReportId = schedule.ReportId,
                RevisionNumber = schedule.RevisionNumber
            },
            Output = ReportServiceModelCloner.CloneOutput(schedule.Output),
            ScheduleId = schedule.Id
        };
        ReportServiceModelCloner.CopyParameters(schedule.ParameterValues, deliveryRequest.ExecutionRequest.ParameterValues);

        for (var index = 0; index < schedule.DeliveryTargetIds.Count; index++)
        {
            deliveryRequest.DeliveryTargetIds.Add(schedule.DeliveryTargetIds[index]);
        }

        result.Delivery = await ExecuteAndDeliverAsync(deliveryRequest, cancellationToken);

        var updated = await _scheduleRepository.UpdateExecutionAsync(schedule.Id, executedAt, cancellationToken);
        if (!updated)
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.ScheduleNotFound,
                $"Schedule '{schedule.Id}' could not be updated after execution.",
                "$.scheduleId"));
        }

        await WriteAuditEntryAsync(
            ReportAuditEventKind.ScheduleCompleted,
            result.HasErrors ? ReportDiagnosticSeverity.Error : ReportDiagnosticSeverity.Info,
            result.HasErrors
                ? $"Schedule '{schedule.Id}' completed with errors."
                : $"Schedule '{schedule.Id}' completed.",
            schedule.ReportId,
            schedule.RevisionNumber,
            schedule.Id,
            deliveryTargetId: null,
            cancellationToken);

        return result;
    }

    private async ValueTask<ReportExecutionEnvironment> BuildExecutionEnvironmentAsync(
        ReportDefinition reportDefinition,
        ReportExecutionEnvironment? requestEnvironment,
        CancellationToken cancellationToken)
    {
        var providerRegistry = requestEnvironment?.ProviderRegistry ?? ProEdit.Reporting.Data.ReportDataProviders.CreateDefaultRegistry();
        var hostDataRegistry = requestEnvironment?.HostDataRegistry ?? new ProEdit.Reporting.Data.ReportHostDataRegistry();
        var environment = new ReportExecutionEnvironment(providerRegistry, hostDataRegistry);

        if (requestEnvironment is not null)
        {
            foreach (var pair in requestEnvironment.Globals)
            {
                environment.Globals[pair.Key] = pair.Value;
            }
        }

        await LoadReferencedReportsAsync(reportDefinition, environment.ReferencedReports, cancellationToken);

        if (requestEnvironment is not null)
        {
            foreach (var pair in requestEnvironment.ReferencedReports)
            {
                environment.ReferencedReports[pair.Key] = pair.Value;
            }
        }

        return environment;
    }

    private async ValueTask LoadReferencedReportsAsync(
        ReportDefinition reportDefinition,
        Dictionary<string, ReportDefinition> target,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        EnqueueReferences(reportDefinition, pending, discovered);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reportId = pending.Pop();
            if (target.ContainsKey(reportId))
            {
                continue;
            }

            var revision = await _reportRepository.GetRevisionAsync(reportId, cancellationToken: cancellationToken);
            if (revision is null)
            {
                continue;
            }

            target[reportId] = revision.ReportDefinition;
            EnqueueReferences(revision.ReportDefinition, pending, discovered);
        }
    }

    private static void EnqueueReferences(
        ReportDefinition reportDefinition,
        Stack<string> pending,
        HashSet<string> discovered)
    {
        for (var sectionIndex = 0; sectionIndex < reportDefinition.Sections.Count; sectionIndex++)
        {
            var section = reportDefinition.Sections[sectionIndex];
            EnqueueItems(section.HeaderItems, pending, discovered);
            EnqueueItems(section.FooterItems, pending, discovered);
            EnqueueItems(section.BodyItems, pending, discovered);
        }
    }

    private static void EnqueueItems(
        IReadOnlyList<ReportItem> items,
        Stack<string> pending,
        HashSet<string> discovered)
    {
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (items[itemIndex] is SubreportItem subreport
                && !string.IsNullOrWhiteSpace(subreport.ReportReferenceId)
                && discovered.Add(subreport.ReportReferenceId))
            {
                pending.Push(subreport.ReportReferenceId);
            }
        }
    }

    private async ValueTask WriteAuditEntryAsync(
        ReportAuditEventKind eventKind,
        ReportDiagnosticSeverity severity,
        string message,
        string? reportId,
        int? revisionNumber,
        string? scheduleId,
        string? deliveryTargetId,
        CancellationToken cancellationToken)
    {
        await _auditLog.WriteAsync(
            new ReportAuditEntry
            {
                Timestamp = _clock.UtcNow,
                EventKind = eventKind,
                Severity = severity,
                Message = message,
                ReportId = reportId,
                RevisionNumber = revisionNumber,
                ScheduleId = scheduleId,
                DeliveryTargetId = deliveryTargetId
            },
            cancellationToken);
    }

    private static ReportExportRequest CreateExportRequest(
        ReportExecutionResult executionResult,
        ReportScheduledOutputDefinition output)
    {
        return new ReportExportRequest
        {
            ExecutionResult = executionResult,
            Format = output.Format,
            Profile = CreateProfile(output)
        };
    }

    private static ReportExportProfile? CreateProfile(ReportScheduledOutputDefinition output)
    {
        return output.Format switch
        {
            ReportExportFormat.Csv => new CsvReportExportProfile
            {
                Delimiter = output.CsvDelimiter,
                IncludeHeaderRows = output.IncludeHeaderRows,
                TablixItemId = output.TablixItemId
            },
            ReportExportFormat.Xlsx => new XlsxReportExportProfile
            {
                IncludeHeaderRows = output.IncludeHeaderRows,
                TablixItemId = output.TablixItemId,
                WorkbookAuthor = output.WorkbookAuthor
            },
            ReportExportFormat.Pdf
                or ReportExportFormat.Docx
                or ReportExportFormat.Html
                or ReportExportFormat.Rtf
                or ReportExportFormat.Markdown
                or ReportExportFormat.Xps
                or ReportExportFormat.Ps => new PaginatedReportExportProfile(),
            _ => null
        };
    }

    private static string ResolveOutputFileName(
        ReportScheduledOutputDefinition output,
        string reportId,
        string reportName,
        string? scheduleId,
        DateTimeOffset timestamp,
        string fileExtension)
    {
        var fileName = string.IsNullOrWhiteSpace(output.FileNamePattern)
            ? ReportServiceModelCloner.SanitizeFileName(string.IsNullOrWhiteSpace(reportName) ? reportId : reportName)
            : ReportServiceModelCloner.ResolveFileNamePattern(
                output.FileNamePattern,
                reportId,
                reportName,
                scheduleId,
                timestamp);

        fileName = ReportServiceModelCloner.SanitizeFileName(fileName);
        if (!fileName.EndsWith(fileExtension, StringComparison.OrdinalIgnoreCase))
        {
            fileName += fileExtension;
        }

        return fileName;
    }
}

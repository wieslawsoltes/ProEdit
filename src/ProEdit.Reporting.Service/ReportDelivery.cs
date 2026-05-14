namespace ProEdit.Reporting.Service;

/// <summary>
/// Well-known delivery channel identifiers.
/// </summary>
public static class ReportDeliveryChannelIds
{
    /// <summary>
    /// File-system delivery channel.
    /// </summary>
    public const string File = "file";

    /// <summary>
    /// In-memory delivery channel for tests and host capture.
    /// </summary>
    public const string InMemory = "in-memory";
}

/// <summary>
/// Defines one stored delivery target.
/// </summary>
public sealed class ReportDeliveryTargetDefinition
{
    /// <summary>
    /// Gets or sets the target identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the delivery channel identifier.
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the target is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets the channel-specific target properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents one exported delivery payload.
/// </summary>
public sealed class ReportDeliveryPayload
{
    /// <summary>
    /// Gets or sets the report identifier.
    /// </summary>
    public string ReportId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the report name.
    /// </summary>
    public string ReportName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional schedule identifier.
    /// </summary>
    public string? ScheduleId { get; set; }

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the recommended file extension.
    /// </summary>
    public string FileExtension { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    public string MediaType { get; set; } = "application/octet-stream";

    /// <summary>
    /// Gets or sets the binary content.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; set; }

    /// <summary>
    /// Gets or sets the UTC payload timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents the outcome of one delivery dispatch.
/// </summary>
public sealed class ReportDeliveryResult
{
    /// <summary>
    /// Gets or sets the target identifier.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the delivery channel identifier.
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved destination text when known.
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes written when known.
    /// </summary>
    public long? BytesWritten { get; set; }

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
/// Delivers one exported reporting payload to a host-managed target.
/// </summary>
public interface IReportDeliveryChannel
{
    /// <summary>
    /// Gets the channel identifier.
    /// </summary>
    string ChannelId { get; }

    /// <summary>
    /// Delivers the supplied payload.
    /// </summary>
    /// <param name="target">The target definition.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The delivery result.</returns>
    ValueTask<ReportDeliveryResult> DeliverAsync(
        ReportDeliveryTargetDefinition target,
        ReportDeliveryPayload payload,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores registered delivery channels.
/// </summary>
public sealed class ReportDeliveryChannelRegistry
{
    private readonly Dictionary<string, IReportDeliveryChannel> _channels =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers one delivery channel.
    /// </summary>
    /// <param name="channel">The channel instance.</param>
    public void Register(IReportDeliveryChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channels[channel.ChannelId] = channel;
    }

    /// <summary>
    /// Attempts to resolve one delivery channel.
    /// </summary>
    /// <param name="channelId">The channel identifier.</param>
    /// <param name="channel">Receives the channel instance.</param>
    /// <returns><see langword="true" /> when the channel exists; otherwise <see langword="false" />.</returns>
    public bool TryGetChannel(string channelId, out IReportDeliveryChannel channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        return _channels.TryGetValue(channelId, out channel!);
    }

    /// <summary>
    /// Creates a registry with the built-in file delivery channel.
    /// </summary>
    /// <returns>The populated registry.</returns>
    public static ReportDeliveryChannelRegistry CreateDefault()
    {
        var registry = new ReportDeliveryChannelRegistry();
        registry.Register(new FileReportDeliveryChannel());
        return registry;
    }
}

/// <summary>
/// Stores delivery target definitions.
/// </summary>
public interface IReportDeliveryTargetRepository
{
    /// <summary>
    /// Saves one target definition.
    /// </summary>
    /// <param name="target">The target definition.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completion task.</returns>
    ValueTask SaveAsync(
        ReportDeliveryTargetDefinition target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one target definition.
    /// </summary>
    /// <param name="targetId">The target identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The target or <see langword="null" />.</returns>
    ValueTask<ReportDeliveryTargetDefinition?> GetAsync(
        string targetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all target definitions.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The targets.</returns>
    ValueTask<IReadOnlyList<ReportDeliveryTargetDefinition>> ListAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory implementation of <see cref="IReportDeliveryTargetRepository" />.
/// </summary>
public sealed class InMemoryReportDeliveryTargetRepository : IReportDeliveryTargetRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ReportDeliveryTargetDefinition> _targets =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask SaveAsync(
        ReportDeliveryTargetDefinition target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ChannelId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _targets[target.Id] = ReportServiceModelCloner.CloneDeliveryTarget(target);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<ReportDeliveryTargetDefinition?> GetAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _targets.TryGetValue(targetId, out var target)
                    ? ReportServiceModelCloner.CloneDeliveryTarget(target)
                    : null);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ReportDeliveryTargetDefinition>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var items = new ReportDeliveryTargetDefinition[_targets.Count];
            var index = 0;
            foreach (var pair in _targets.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                items[index++] = ReportServiceModelCloner.CloneDeliveryTarget(pair.Value);
            }

            return ValueTask.FromResult<IReadOnlyList<ReportDeliveryTargetDefinition>>(items);
        }
    }
}

/// <summary>
/// Built-in file-system delivery channel.
/// </summary>
public sealed class FileReportDeliveryChannel : IReportDeliveryChannel
{
    /// <inheritdoc />
    public string ChannelId => ReportDeliveryChannelIds.File;

    /// <inheritdoc />
    public async ValueTask<ReportDeliveryResult> DeliverAsync(
        ReportDeliveryTargetDefinition target,
        ReportDeliveryPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new ReportDeliveryResult
        {
            TargetId = target.Id,
            ChannelId = ChannelId
        };

        if (!target.Properties.TryGetValue("directory", out var directory)
            || string.IsNullOrWhiteSpace(directory))
        {
            result.Diagnostics.Add(new ReportDiagnostic(
                ReportDiagnosticSeverity.Error,
                ReportDiagnosticCodes.DeliveryFailed,
                "File delivery targets require a non-empty 'directory' property.",
                "$.properties.directory"));
            return result;
        }

        var fileName = payload.FileName;
        if (target.Properties.TryGetValue("fileNamePattern", out var pattern)
            && !string.IsNullOrWhiteSpace(pattern))
        {
            fileName = ReportServiceModelCloner.ResolveFileNamePattern(
                pattern,
                payload.ReportId,
                payload.ReportName,
                payload.ScheduleId,
                payload.CreatedAt);
        }

        fileName = ReportServiceModelCloner.SanitizeFileName(fileName);
        if (!fileName.EndsWith(payload.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            fileName += payload.FileExtension;
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);

        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            useAsync: true);
        await stream.WriteAsync(payload.Content, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        result.Destination = path;
        result.BytesWritten = payload.Content.Length;
        return result;
    }
}

/// <summary>
/// Captures one in-memory delivery payload.
/// </summary>
public sealed class InMemoryReportDeliveryRecord
{
    /// <summary>
    /// Gets or sets the target identifier.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the delivered payload.
    /// </summary>
    public ReportDeliveryPayload Payload { get; set; } = new();
}

/// <summary>
/// In-memory delivery channel used for tests and host-side capture.
/// </summary>
public sealed class InMemoryReportDeliveryChannel : IReportDeliveryChannel
{
    private readonly object _gate = new();
    private readonly List<InMemoryReportDeliveryRecord> _deliveries = new();

    /// <inheritdoc />
    public string ChannelId => ReportDeliveryChannelIds.InMemory;

    /// <summary>
    /// Gets the captured deliveries.
    /// </summary>
    public IReadOnlyList<InMemoryReportDeliveryRecord> Deliveries
    {
        get
        {
            lock (_gate)
            {
                var items = new InMemoryReportDeliveryRecord[_deliveries.Count];
                for (var index = 0; index < _deliveries.Count; index++)
                {
                    items[index] = CloneRecord(_deliveries[index]);
                }

                return items;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<ReportDeliveryResult> DeliverAsync(
        ReportDeliveryTargetDefinition target,
        ReportDeliveryPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _deliveries.Add(new InMemoryReportDeliveryRecord
            {
                TargetId = target.Id,
                Payload = ClonePayload(payload)
            });
        }

        return ValueTask.FromResult(new ReportDeliveryResult
        {
            TargetId = target.Id,
            ChannelId = ChannelId,
            Destination = ChannelId,
            BytesWritten = payload.Content.Length
        });
    }

    private static InMemoryReportDeliveryRecord CloneRecord(InMemoryReportDeliveryRecord record)
    {
        return new InMemoryReportDeliveryRecord
        {
            TargetId = record.TargetId,
            Payload = ClonePayload(record.Payload)
        };
    }

    private static ReportDeliveryPayload ClonePayload(ReportDeliveryPayload payload)
    {
        return new ReportDeliveryPayload
        {
            ReportId = payload.ReportId,
            ReportName = payload.ReportName,
            ScheduleId = payload.ScheduleId,
            FileName = payload.FileName,
            FileExtension = payload.FileExtension,
            MediaType = payload.MediaType,
            CreatedAt = payload.CreatedAt,
            Content = payload.Content.ToArray()
        };
    }
}

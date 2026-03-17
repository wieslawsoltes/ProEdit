using System.Globalization;
using System.Text;
using Vibe.Office.Reporting.Export;
using Vibe.Office.Reporting.Service;
using Xunit;

namespace Vibe.Office.Reporting.Service.Tests;

public sealed class ReportServiceTests
{
    [Fact]
    public async Task InMemoryReportRepository_SaveAsync_CreatesRevisionsAndClonesDefinitions()
    {
        var clock = new FixedReportClock(new DateTimeOffset(2026, 3, 17, 9, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryReportRepository(clock: clock);
        var definition = CreateStaticReport("invoice", "Invoice", "Version 1");

        var firstSave = await repository.SaveAsync(new ReportRepositorySaveRequest
        {
            ReportDefinition = definition,
            Comment = "Initial"
        });

        definition.Name = "Mutated";
        ((TextItem)definition.Sections[0].BodyItems[0]).StaticText = "Changed";

        var latestAfterFirstSave = await repository.GetRevisionAsync("invoice");

        Assert.False(firstSave.HasErrors);
        Assert.NotNull(firstSave.Revision);
        Assert.Equal(1, firstSave.Revision!.RevisionNumber);
        Assert.Equal("Invoice", latestAfterFirstSave!.ReportDefinition.Name);
        Assert.Equal("Version 1", Assert.IsType<TextItem>(latestAfterFirstSave.ReportDefinition.Sections[0].BodyItems[0]).StaticText);

        var secondDefinition = CreateStaticReport("invoice", "Invoice v2", "Version 2");
        var secondSave = await repository.SaveAsync(new ReportRepositorySaveRequest
        {
            ReportDefinition = secondDefinition,
            Comment = "Second"
        });

        var firstRevision = await repository.GetRevisionAsync("invoice", 1);
        var secondRevision = await repository.GetRevisionAsync("invoice", 2);

        Assert.NotNull(secondSave.Revision);
        Assert.Equal(2, secondSave.Revision!.RevisionNumber);
        Assert.Equal("Version 1", Assert.IsType<TextItem>(firstRevision!.ReportDefinition.Sections[0].BodyItems[0]).StaticText);
        Assert.Equal("Version 2", Assert.IsType<TextItem>(secondRevision!.ReportDefinition.Sections[0].BodyItems[0]).StaticText);
    }

    [Fact]
    public async Task ReportExecutor_ExecuteAsync_ComposesDocumentAndMaterializedReport()
    {
        var environment = ReportExecutionEnvironment.CreateDefault();
        var executor = new ReportExecutor(environment);
        var report = new ReportDefinition
        {
            Id = "greeting",
            Name = "Greeting",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Customer",
                    DisplayName = "Customer",
                    DataType = ReportParameterDataType.String
                }
            },
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new TextItem
                        {
                            Id = "body",
                            ValueExpression = "'Hello ' + Parameters.Customer",
                            Bounds = new ReportItemBounds(0f, 0f, 240f, 24f)
                        }
                    }
                }
            }
        };

        var request = new ReportExecutionRequest
        {
            ReportDefinition = report,
            Culture = CultureInfo.InvariantCulture,
            UiCulture = CultureInfo.InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        };
        request.ParameterValues["Customer"] = ReportParameterValue.FromScalar("Ada");

        var result = await executor.ExecuteAsync(request);

        Assert.NotNull(result.MaterializedReport);
        Assert.NotNull(result.Document);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == ReportDiagnosticSeverity.Error);
        var text = Assert.IsType<MaterializedTextReportItem>(result.MaterializedReport!.Sections[0].BodyItems[0]);
        Assert.Equal("Hello Ada", text.Text);
        Assert.True(result.Metrics.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ReportService_ExecuteAsync_LoadsReferencedSubreportsFromRepository()
    {
        var repository = new InMemoryReportRepository();
        await repository.SaveAsync(new ReportRepositorySaveRequest
        {
            ReportDefinition = CreateStaticReport("detail-report", "Detail Report", "Subreport body")
        });

        var rootReport = new ReportDefinition
        {
            Id = "root-report",
            Name = "Root Report",
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new SubreportItem
                        {
                            Id = "subreport",
                            ReportReferenceId = "detail-report",
                            Bounds = new ReportItemBounds(0f, 0f, 320f, 200f)
                        }
                    }
                }
            }
        };
        await repository.SaveAsync(new ReportRepositorySaveRequest
        {
            ReportDefinition = rootReport
        });

        var service = new ReportService(repository);
        var result = await service.ExecuteAsync(new ReportServiceExecutionRequest
        {
            ReportId = "root-report",
            Culture = CultureInfo.InvariantCulture,
            UiCulture = CultureInfo.InvariantCulture,
            TimeZone = TimeZoneInfo.Utc
        });

        Assert.False(result.HasErrors);
        var subreport = Assert.IsType<MaterializedSubreportReportItem>(result.ExecutionResult.MaterializedReport!.Sections[0].BodyItems[0]);
        Assert.NotNull(subreport.Report);
        var text = Assert.IsType<MaterializedTextReportItem>(subreport.Report!.Sections[0].BodyItems[0]);
        Assert.Equal("Subreport body", text.Text);
    }

    [Fact]
    public async Task ReportService_ExecuteAndDeliverAsync_DispatchesToInMemoryChannelAndWritesAudit()
    {
        var repository = new InMemoryReportRepository();
        await repository.SaveAsync(new ReportRepositorySaveRequest
        {
            ReportDefinition = CreateStaticReport("deliverable", "Deliverable", "Hello delivery")
        });

        var targetRepository = new InMemoryReportDeliveryTargetRepository();
        await targetRepository.SaveAsync(new ReportDeliveryTargetDefinition
        {
            Id = "memory-target",
            Name = "Memory Target",
            ChannelId = ReportDeliveryChannelIds.InMemory
        });

        var channel = new InMemoryReportDeliveryChannel();
        var registry = ReportDeliveryChannelRegistry.CreateDefault();
        registry.Register(channel);
        var auditLog = new InMemoryReportAuditLog();
        var service = new ReportService(
            repository,
            deliveryTargetRepository: targetRepository,
            deliveryChannels: registry,
            auditLog: auditLog);

        var delivery = await service.ExecuteAndDeliverAsync(new ReportServiceDeliveryRequest
        {
            ExecutionRequest = new ReportServiceExecutionRequest
            {
                ReportId = "deliverable",
                Culture = CultureInfo.InvariantCulture,
                UiCulture = CultureInfo.InvariantCulture,
                TimeZone = TimeZoneInfo.Utc
            },
            Output = new ReportScheduledOutputDefinition
            {
                Format = ReportExportFormat.Markdown,
                FileNamePattern = "{reportName}-{timestamp}"
            },
            DeliveryTargetIds =
            {
                "memory-target"
            }
        });

        Assert.False(delivery.HasErrors);
        Assert.Single(delivery.DeliveryResults);
        Assert.Single(channel.Deliveries);
        var payloadText = Encoding.UTF8.GetString(channel.Deliveries[0].Payload.Content.ToArray());
        Assert.Contains("Hello delivery", payloadText, StringComparison.OrdinalIgnoreCase);

        var auditEntries = await auditLog.ListAsync();
        Assert.Contains(auditEntries, static entry => entry.EventKind == ReportAuditEventKind.ExecutionCompleted);
        Assert.Contains(auditEntries, static entry => entry.EventKind == ReportAuditEventKind.DeliveryCompleted);
    }

    [Fact]
    public async Task ReportService_RunDueSchedulesAsync_ExecutesDueScheduleAndAdvancesNextRun()
    {
        var clock = new FixedReportClock(new DateTimeOffset(2026, 3, 17, 12, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryReportRepository(clock: clock);
        await repository.SaveAsync(new ReportRepositorySaveRequest
        {
            ReportDefinition = CreateStaticReport("scheduled-report", "Scheduled Report", "Scheduled body")
        });

        var targetRepository = new InMemoryReportDeliveryTargetRepository();
        await targetRepository.SaveAsync(new ReportDeliveryTargetDefinition
        {
            Id = "memory-target",
            Name = "Memory Target",
            ChannelId = ReportDeliveryChannelIds.InMemory
        });

        var scheduleRepository = new InMemoryReportScheduleRepository();
        await scheduleRepository.SaveAsync(new ReportScheduleDefinition
        {
            Id = "hourly",
            Name = "Hourly",
            ReportId = "scheduled-report",
            StartsAt = clock.UtcNow.AddHours(-1),
            Interval = TimeSpan.FromHours(1),
            Output = new ReportScheduledOutputDefinition
            {
                Format = ReportExportFormat.Markdown
            },
            DeliveryTargetIds =
            {
                "memory-target"
            }
        });

        var channel = new InMemoryReportDeliveryChannel();
        var registry = ReportDeliveryChannelRegistry.CreateDefault();
        registry.Register(channel);
        var auditLog = new InMemoryReportAuditLog();
        var service = new ReportService(
            repository,
            scheduleRepository,
            targetRepository,
            registry,
            auditLog,
            clock);

        var runs = await service.RunDueSchedulesAsync();

        Assert.Single(runs);
        Assert.False(runs[0].HasErrors);
        Assert.Single(channel.Deliveries);

        var updatedSchedule = await scheduleRepository.GetAsync("hourly");
        Assert.NotNull(updatedSchedule);
        Assert.Equal(clock.UtcNow, updatedSchedule!.LastRunAt);
        Assert.Equal(clock.UtcNow.AddHours(1), updatedSchedule.NextRunAt);

        var auditEntries = await auditLog.ListAsync();
        Assert.Contains(auditEntries, static entry => entry.EventKind == ReportAuditEventKind.ScheduleCompleted);
    }

    [Fact]
    public async Task InMemoryReportScheduleRepository_ListDueAsync_DoesNotReturnEndedSchedules()
    {
        var repository = new InMemoryReportScheduleRepository();
        await repository.SaveAsync(new ReportScheduleDefinition
        {
            Id = "ended",
            Name = "Ended",
            ReportId = "report",
            StartsAt = new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 3, 17, 8, 30, 0, TimeSpan.Zero),
            Interval = TimeSpan.FromHours(1)
        });

        await repository.UpdateExecutionAsync(
            "ended",
            new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero));

        var due = await repository.ListDueAsync(new DateTimeOffset(2026, 3, 17, 12, 0, 0, TimeSpan.Zero));
        Assert.Empty(due);
    }

    [Fact]
    public async Task InMemoryReportScheduleRepository_SaveAsync_DoesNotResurrectTerminalSchedules()
    {
        var repository = new InMemoryReportScheduleRepository();
        var schedule = new ReportScheduleDefinition
        {
            Id = "terminal",
            Name = "Terminal",
            ReportId = "report",
            StartsAt = new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 3, 17, 8, 30, 0, TimeSpan.Zero),
            Interval = TimeSpan.FromHours(1),
            LastRunAt = new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero),
            NextRunAt = null
        };

        await repository.SaveAsync(schedule);

        var stored = await repository.GetAsync("terminal");
        Assert.NotNull(stored);
        Assert.Null(stored!.NextRunAt);

        var due = await repository.ListDueAsync(new DateTimeOffset(2026, 3, 17, 12, 0, 0, TimeSpan.Zero));
        Assert.Empty(due);
    }

    [Fact]
    public async Task ReportService_ExecuteAndDeliverAsync_ConvertsChannelExceptionsIntoDiagnostics()
    {
        var repository = new InMemoryReportRepository();
        await repository.SaveAsync(new ReportRepositorySaveRequest
        {
            ReportDefinition = CreateStaticReport("failure-report", "Failure Report", "Body")
        });

        var targetRepository = new InMemoryReportDeliveryTargetRepository();
        await targetRepository.SaveAsync(new ReportDeliveryTargetDefinition
        {
            Id = "throw-target",
            Name = "Throw Target",
            ChannelId = "throw"
        });

        var registry = ReportDeliveryChannelRegistry.CreateDefault();
        registry.Register(new ThrowingDeliveryChannel());
        var auditLog = new InMemoryReportAuditLog();
        var service = new ReportService(
            repository,
            deliveryTargetRepository: targetRepository,
            deliveryChannels: registry,
            auditLog: auditLog);

        var result = await service.ExecuteAndDeliverAsync(new ReportServiceDeliveryRequest
        {
            ExecutionRequest = new ReportServiceExecutionRequest
            {
                ReportId = "failure-report",
                Culture = CultureInfo.InvariantCulture,
                UiCulture = CultureInfo.InvariantCulture,
                TimeZone = TimeZoneInfo.Utc
            },
            Output = new ReportScheduledOutputDefinition
            {
                Format = ReportExportFormat.Markdown
            },
            DeliveryTargetIds =
            {
                "throw-target"
            }
        });

        Assert.True(result.HasErrors);
        Assert.Single(result.DeliveryResults);
        Assert.Contains(
            result.DeliveryResults[0].Diagnostics,
            static diagnostic => diagnostic.Code == ReportDiagnosticCodes.DeliveryFailed);

        var auditEntries = await auditLog.ListAsync();
        Assert.Contains(
            auditEntries,
            static entry => entry.EventKind == ReportAuditEventKind.DeliveryCompleted
                && entry.Severity == ReportDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ReportService_ExecuteAndDeliverAsync_ConvertsExporterExceptionsIntoDiagnostics()
    {
        var repository = new InMemoryReportRepository();
        await repository.SaveAsync(new ReportRepositorySaveRequest
        {
            ReportDefinition = CreateStaticReport("export-failure", "Export Failure", "Body")
        });

        var service = new ReportService(
            repository,
            exporter: new ThrowingExporter());

        var result = await service.ExecuteAndDeliverAsync(new ReportServiceDeliveryRequest
        {
            ExecutionRequest = new ReportServiceExecutionRequest
            {
                ReportId = "export-failure",
                Culture = CultureInfo.InvariantCulture,
                UiCulture = CultureInfo.InvariantCulture,
                TimeZone = TimeZoneInfo.Utc
            },
            Output = new ReportScheduledOutputDefinition
            {
                Format = ReportExportFormat.Markdown
            }
        });

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == ReportDiagnosticCodes.ExportFailed);
        Assert.Empty(result.DeliveryResults);
    }

    [Fact]
    public async Task FileReportDeliveryChannel_DeliverAsync_WritesExpectedFile()
    {
        var channel = new FileReportDeliveryChannel();
        var directory = Path.Combine(Path.GetTempPath(), "vibeoffice-report-service-tests", Guid.NewGuid().ToString("N"));
        var target = new ReportDeliveryTargetDefinition
        {
            Id = "file-target",
            Name = "File Target",
            ChannelId = ReportDeliveryChannelIds.File
        };
        target.Properties["directory"] = directory;
        target.Properties["fileNamePattern"] = "{reportName}-{timestamp}";

        var payload = new ReportDeliveryPayload
        {
            ReportId = "sales",
            ReportName = "Sales Report",
            FileExtension = ".txt",
            MediaType = "text/plain",
            FileName = "ignored.txt",
            Content = Encoding.UTF8.GetBytes("payload"),
            CreatedAt = new DateTimeOffset(2026, 3, 17, 15, 30, 0, TimeSpan.Zero)
        };

        try
        {
            var result = await channel.DeliverAsync(target, payload);

            Assert.False(result.HasErrors);
            Assert.NotNull(result.Destination);
            Assert.True(File.Exists(result.Destination));
            Assert.Equal("payload", await File.ReadAllTextAsync(result.Destination!));
            Assert.EndsWith(".txt", result.Destination, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ReportDefinition CreateStaticReport(
        string id,
        string name,
        string text)
    {
        return new ReportDefinition
        {
            Id = id,
            Name = name,
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main",
                    BodyItems =
                    {
                        new TextItem
                        {
                            Id = "body",
                            StaticText = text,
                            Bounds = new ReportItemBounds(0f, 0f, 240f, 24f)
                        }
                    }
                }
            }
        };
    }

    private sealed class FixedReportClock : IReportClock
    {
        public FixedReportClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class ThrowingDeliveryChannel : IReportDeliveryChannel
    {
        public string ChannelId => "throw";

        public ValueTask<ReportDeliveryResult> DeliverAsync(
            ReportDeliveryTargetDefinition target,
            ReportDeliveryPayload payload,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Delivery channel failure.");
        }
    }

    private sealed class ThrowingExporter : IReportExporter
    {
        public ValueTask<ReportExportResult> ExportAsync(
            ReportExportRequest request,
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Exporter failure.");
        }
    }
}

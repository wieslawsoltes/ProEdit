using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vibe.Office.Documents;
using Vibe.Office.Printing;
using Vibe.Office.Reporting.Avalonia.Viewer;
using Vibe.Office.Reporting.Export;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Vibe.Office.Reporting.Avalonia.Headless.Tests.HeadlessTestAppBuilder))]

namespace Vibe.Office.Reporting.Avalonia.Headless.Tests;

public sealed class HeadlessTestApp : Application
{
}

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HeadlessTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
    }
}

public sealed class ReportViewerControlHeadlessTests
{
    [AvaloniaFact]
    public async Task ViewerControl_LoadsPreviewDocumentMapAndSearch()
    {
        using var viewModel = new ReportViewerViewModel(new ReportViewerSessionService());
        var source = CreateViewerSource();
        await viewModel.LoadAsync(source);

        var control = new ReportViewerControl
        {
            DataContext = viewModel,
            Width = 1400,
            Height = 900
        };
        var window = new Window
        {
            Width = 1440,
            Height = 960,
            Content = control
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        viewModel.SearchQuery = "Preview Title";
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Single(viewModel.Pages);
        Assert.NotEmpty(viewModel.DocumentMapEntries);
        Assert.NotEmpty(viewModel.SearchResults);
        Assert.NotNull(control.GetVisualDescendants().OfType<Image>().FirstOrDefault(image => image.Source is not null));

        window.Close();
    }

    [AvaloniaFact]
    public async Task Viewer_ApplyParametersRefreshesSearchAndStateRoundTrips()
    {
        var service = new ReportViewerSessionService();
        using var viewModel = new ReportViewerViewModel(service);
        var source = CreateViewerSource();
        await viewModel.LoadAsync(source);

        var parameter = Assert.Single(viewModel.Parameters);
        parameter.TextValue = "Updated Title";

        await viewModel.ApplyParametersAsync();
        viewModel.SearchQuery = "Updated Title";

        Assert.NotEmpty(viewModel.SearchResults);
        Assert.Equal("Updated Title", viewModel.Parameters[0].TextValue);

        viewModel.ZoomFactor = 1.5f;
        viewModel.SelectedPaneIndex = (int)ReportViewerPane.Search;
        var state = viewModel.CaptureState();

        using var restoredViewModel = new ReportViewerViewModel(service);
        await restoredViewModel.LoadAsync(CreateViewerSource());
        restoredViewModel.ApplyState(state);

        Assert.Equal(1.5f, restoredViewModel.ZoomFactor);
        Assert.Equal("Updated Title", restoredViewModel.SearchQuery);
        Assert.Equal((int)ReportViewerPane.Search, restoredViewModel.SelectedPaneIndex);
    }

    [AvaloniaFact]
    public async Task Viewer_DrillthroughAndBackNavigationWork()
    {
        using var viewModel = new ReportViewerViewModel(new ReportViewerSessionService());
        await viewModel.LoadAsync(CreateViewerSource());

        var drillthrough = Assert.Single(viewModel.DrillthroughItems);
        await viewModel.NavigateToDrillthroughAsync(drillthrough.Entry);

        Assert.NotNull(viewModel.Source);
        Assert.Equal("detail-report", viewModel.Source!.ReportDefinition.Id);
        viewModel.SearchQuery = "Detail Title";
        Assert.NotEmpty(viewModel.SearchResults);

        await viewModel.RefreshAsync();
        viewModel.SearchQuery = "Detail Title";
        Assert.NotEmpty(viewModel.SearchResults);

        await viewModel.GoBackAsync();

        Assert.Equal("main-report", viewModel.Source!.ReportDefinition.Id);
        viewModel.SearchQuery = "Preview Title";
        Assert.NotEmpty(viewModel.SearchResults);
    }

    [AvaloniaFact]
    public async Task Viewer_ExportsAndBuildsPrintOutput()
    {
        using var viewModel = new ReportViewerViewModel(new ReportViewerSessionService());
        await viewModel.LoadAsync(CreateViewerSource());

        viewModel.SelectedExportFormat = ReportExportFormat.Pdf;
        await viewModel.ExportAsync();
        Assert.False(string.IsNullOrWhiteSpace(viewModel.LastExportPath));
        Assert.True(File.Exists(viewModel.LastExportPath));

        await viewModel.PrintAsync();
        Assert.False(string.IsNullOrWhiteSpace(viewModel.LastPrintedOutputPath));
        Assert.True(File.Exists(viewModel.LastPrintedOutputPath));
    }

    [AvaloniaFact]
    public async Task Viewer_FailedDrillthroughPreservesCurrentState()
    {
        using var viewModel = new ReportViewerViewModel(new FailingViewerSessionService(
            execute: (source, _) => source.ReportDefinition.Id switch
            {
                "main-report" => CreateStubSnapshot(includeDrillthrough: true),
                "detail-report" => throw new InvalidOperationException("Detail report execution failed."),
                _ => throw new InvalidOperationException("Unexpected report.")
            }));

        await viewModel.LoadAsync(CreateViewerSource());

        Assert.Equal("main-report", viewModel.Source!.ReportDefinition.Id);
        Assert.False(viewModel.CanGoBack);

        var drillthrough = Assert.Single(viewModel.DrillthroughItems);
        await viewModel.NavigateToDrillthroughAsync(drillthrough.Entry);

        Assert.Equal("main-report", viewModel.Source!.ReportDefinition.Id);
        Assert.False(viewModel.CanGoBack);
        Assert.Contains(viewModel.Diagnostics, diagnostic =>
            diagnostic.Code == ReportDiagnosticCodes.ViewerOperationFailed &&
            diagnostic.Path == "$viewer.load");
        Assert.Equal("Detail report execution failed.", viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task Viewer_ExportAndPrintFailuresDoNotLeakStateOrFiles()
    {
        var exportPath = GetExpectedOutputPath("Main Report", ReportExportFormat.Pdf);
        var printPath = GetExpectedOutputPath("Main Report-print", ReportExportFormat.Pdf);
        DeleteFileIfExists(exportPath);
        DeleteFileIfExists(printPath);

        using var viewModel = new ReportViewerViewModel(new FailingViewerSessionService(
            export: (_, _, stream) =>
            {
                stream.WriteByte(0x42);
                throw new IOException("Export writer failed.");
            },
            print: (_, settings) =>
            {
                if (!string.IsNullOrWhiteSpace(settings.OutputPath))
                {
                    File.WriteAllText(settings.OutputPath, "partial");
                }

                return PrintJobResult.Failed("Print pipeline failed.");
            }));

        await viewModel.LoadAsync(CreateViewerSource());
        viewModel.SelectedExportFormat = ReportExportFormat.Pdf;

        await viewModel.ExportAsync();

        Assert.Null(viewModel.LastExportPath);
        Assert.False(File.Exists(exportPath));
        Assert.Contains(viewModel.Diagnostics, diagnostic =>
            diagnostic.Code == ReportDiagnosticCodes.ViewerOperationFailed &&
            diagnostic.Path == "$viewer.export");
        Assert.Equal("Export writer failed.", viewModel.StatusMessage);

        await viewModel.PrintAsync();

        Assert.Null(viewModel.LastPrintedOutputPath);
        Assert.False(File.Exists(printPath));
        Assert.Equal("Print pipeline failed.", viewModel.StatusMessage);
    }

    private static ReportViewerSource CreateViewerSource()
    {
        var detailReport = new ReportDefinition
        {
            Id = "detail-report",
            Name = "Detail Report",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Title",
                    DisplayName = "Title",
                    DataType = ReportParameterDataType.String,
                    DefaultValueExpression = "'Detail Default'",
                    Visibility = ReportParameterVisibility.Hidden
                }
            },
            Sections =
            {
                new ReportSection
                {
                    Id = "detail",
                    Name = "Detail",
                    BookmarkExpression = "'detail-section'",
                    BodyItems =
                    {
                        new TextItem
                        {
                            Id = "detail-title",
                            Name = "Detail Title",
                            ValueExpression = "Parameters.Title",
                            BookmarkExpression = "'detail-title'",
                            Bounds = new ReportItemBounds(0f, 0f, 300f, 24f)
                        }
                    }
                }
            }
        };

        var mainReport = new ReportDefinition
        {
            Id = "main-report",
            Name = "Main Report",
            Parameters =
            {
                new ReportParameterDefinition
                {
                    Id = "Title",
                    DisplayName = "Title",
                    Prompt = "Preview Title",
                    DataType = ReportParameterDataType.String,
                    DefaultValueExpression = "'Preview Title'",
                    Visibility = ReportParameterVisibility.Visible
                }
            },
            Sections =
            {
                new ReportSection
                {
                    Id = "main",
                    Name = "Main Section",
                    BookmarkExpression = "'main-section'",
                    BodyItems =
                    {
                        new TextItem
                        {
                            Id = "title",
                            Name = "Title",
                            ValueExpression = "Parameters.Title",
                            BookmarkExpression = "'summary-title'",
                            Bounds = new ReportItemBounds(0f, 0f, 300f, 24f)
                        },
                        new TextItem
                        {
                            Id = "drillthrough",
                            Name = "Open Details",
                            StaticText = "Open Details",
                            BookmarkExpression = "'details-link'",
                            DrillthroughAction = new ReportDrillthroughAction
                            {
                                ReportReferenceId = "detail-report",
                                Parameters =
                                {
                                    new ReportParameterBinding
                                    {
                                        ParameterId = "Title",
                                        ValueExpression = "'Detail Title'"
                                    }
                                }
                            },
                            Bounds = new ReportItemBounds(0f, 40f, 300f, 24f)
                        }
                    }
                }
            }
        };

        var source = new ReportViewerSource
        {
            ReportDefinition = mainReport
        };
        source.ReferencedReports[detailReport.Id] = detailReport;
        return source;
    }

    private static ReportViewerExecutionSnapshot CreateStubSnapshot(bool includeDrillthrough = false)
    {
        var snapshot = new ReportViewerExecutionSnapshot
        {
            ExecutionResult = new ReportExecutionResult
            {
                Document = new Document()
            }
        };

        snapshot.SearchEntries.Add(new ReportViewerSearchEntry
        {
            Text = "Preview Title",
            PageIndex = 0,
            ParagraphIndex = 0
        });

        snapshot.DocumentMapEntries.Add(new ReportViewerDocumentMapEntry
        {
            Label = "Main Section",
            Bookmark = "main-section",
            PageIndex = 0,
            Level = 0,
            SourceItemId = "main"
        });

        if (includeDrillthrough)
        {
            var action = new MaterializedReportDrillthroughAction
            {
                ReportReferenceId = "detail-report"
            };
            action.Parameters["Title"] = ReportParameterValue.FromScalar("Detail Title");

            snapshot.DrillthroughEntries.Add(new ReportViewerDrillthroughEntry
            {
                Label = "Open Details",
                PageIndex = 0,
                SourceItemId = "drillthrough",
                Action = action
            });
        }

        return snapshot;
    }

    private static string GetExpectedOutputPath(string reportName, ReportExportFormat format)
    {
        var extension = format switch
        {
            ReportExportFormat.Pdf => ".pdf",
            ReportExportFormat.Docx => ".docx",
            ReportExportFormat.Html => ".html",
            ReportExportFormat.Rtf => ".rtf",
            ReportExportFormat.Markdown => ".md",
            ReportExportFormat.Xps => ".xps",
            ReportExportFormat.Ps => ".ps",
            ReportExportFormat.Csv => ".csv",
            ReportExportFormat.Xlsx => ".xlsx",
            _ => ".bin"
        };

        return Path.Combine(Path.GetTempPath(), reportName + extension);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class FailingViewerSessionService : IReportViewerSessionService
    {
        private readonly Func<ReportViewerSource, IReadOnlyDictionary<string, ReportParameterValue>, ReportViewerExecutionSnapshot> _execute;
        private readonly Func<ReportViewerExecutionSnapshot, ReportExportRequest, Stream, ReportExportResult> _export;
        private readonly Func<ReportViewerExecutionSnapshot, PrintSettings, PrintJobResult> _print;

        public FailingViewerSessionService(
            Func<ReportViewerSource, IReadOnlyDictionary<string, ReportParameterValue>, ReportViewerExecutionSnapshot>? execute = null,
            Func<ReportViewerExecutionSnapshot, ReportExportRequest, Stream, ReportExportResult>? export = null,
            Func<ReportViewerExecutionSnapshot, PrintSettings, PrintJobResult>? print = null)
        {
            _execute = execute ?? ((_, _) => CreateStubSnapshot());
            _export = export ?? ((_, _, _) => new ReportExportResult
            {
                FileExtension = ".pdf",
                MediaType = "application/pdf"
            });
            _print = print ?? ((_, settings) => PrintJobResult.Success(settings.OutputPath));
        }

        public ValueTask<ReportViewerParameterResolutionResult> ResolveParametersAsync(
            ReportViewerSource source,
            IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            var result = new ReportViewerParameterResolutionResult();
            foreach (var definition in source.ReportDefinition.Parameters)
            {
                result.Parameters.Add(new ReportViewerParameterState
                {
                    Definition = definition,
                    ResolvedValue = suppliedParameters.TryGetValue(definition.Id, out var suppliedValue)
                        ? suppliedValue
                        : null
                });
            }

            return ValueTask.FromResult(result);
        }

        public ValueTask<ReportViewerExecutionSnapshot> ExecuteAsync(
            ReportViewerSource source,
            IReadOnlyDictionary<string, ReportParameterValue> suppliedParameters,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_execute(source, suppliedParameters));
        }

        public ValueTask<ReportExportResult> ExportAsync(
            ReportViewerExecutionSnapshot snapshot,
            ReportExportRequest request,
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_export(snapshot, request, stream));
        }

        public ValueTask<PrintJobResult> PrintAsync(
            ReportViewerExecutionSnapshot snapshot,
            PrintSettings settings,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_print(snapshot, settings));
        }
    }
}

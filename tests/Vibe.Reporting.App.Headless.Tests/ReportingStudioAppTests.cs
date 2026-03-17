using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Reactive;
using ReactiveUI;
using Vibe.Office.Documents;
using Vibe.Office.Reporting;
using Vibe.Office.Reporting.Avalonia.Designer;
using Vibe.Office.Reporting.Avalonia.Viewer;
using Vibe.Office.Reporting.Data;
using Vibe.Office.Reporting.Rdl;
using Vibe.Office.Reporting.Serialization;
using Vibe.Reporting.App.Controls;
using Vibe.Reporting.App.Services;
using Vibe.Reporting.App.ViewModels;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Vibe.Reporting.App.Headless.Tests.HeadlessTestAppBuilder))]

namespace Vibe.Reporting.App.Headless.Tests;

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

public sealed class ReportingStudioAppTests
{
    private static readonly string SampleCorpusPath = ResolveSampleCorpusPath();

    [Fact]
    public async Task DocumentService_OpenNative_LoadsSiblingReferencedReports()
    {
        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var sampleWorkspace = workspaceFactory.CreateSampleWorkspace();

        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sales-overview.vreport.json");
        var detailPath = Path.Combine(Path.GetDirectoryName(rootPath)!, "regional-detail.vreport.json");

        try
        {
            await service.SaveNativeAsync(sampleWorkspace.Source.ReportDefinition, rootPath);
            await service.SaveNativeAsync(sampleWorkspace.Source.ReferencedReports["regional-detail"], detailPath);

            var opened = await service.OpenNativeAsync(rootPath);

            Assert.NotNull(opened.Workspace);
            Assert.Contains("regional-detail", opened.Workspace!.Source.ReferencedReports.Keys);
            Assert.False(opened.HasErrors);
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(rootPath)!);
        }
    }

    [AvaloniaFact]
    public async Task ViewModel_Initialize_SaveAndExport_FlowWorks()
    {
        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var outputRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var picker = new StubReportingStudioFilePickerService
        {
            SaveTemplatePath = Path.Combine(outputRoot, "studio-sample"),
            ExportRdlPath = Path.Combine(outputRoot, "studio-sample-export")
        };

        try
        {
            using var viewModel = new ReportingStudioViewModel(picker, service);
            await viewModel.InitializeAsync();

            Assert.NotNull(viewModel.ViewerViewModel.CurrentSnapshot);
            Assert.False(viewModel.DesignerViewModel.IsPreviewDirty);

            await ExecuteAsync(viewModel.SaveTemplateAsCommand);
            await ExecuteAsync(viewModel.ExportRdlCommand);

            Assert.True(File.Exists(Path.Combine(outputRoot, "studio-sample.vreport.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "studio-sample-export.rdl")));
        }
        finally
        {
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_RendersStudioShell()
    {
        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        using var viewModel = new ReportingStudioViewModel(new StubReportingStudioFilePickerService(), service);
        await viewModel.InitializeAsync();

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        window.Show();
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.NotNull(window.GetVisualDescendants().OfType<ReportingStudioHeaderBar>().SingleOrDefault());
        Assert.NotNull(window.GetVisualDescendants().OfType<ReportingStudioCommandBar>().SingleOrDefault());
        Assert.Empty(window.GetVisualDescendants().OfType<ReportingStudioSidebar>());
        Assert.NotNull(window.GetVisualDescendants().OfType<ReportingStudioStatusStrip>().SingleOrDefault());
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => string.Equals(text.Text, "VibeOffice Reporting Studio", StringComparison.Ordinal));
        Assert.NotNull(viewModel.ViewerViewModel.CurrentSnapshot);
        Assert.NotEmpty(viewModel.ViewerViewModel.Pages);

        window.Close();
    }

    [Fact]
    public async Task SampleCorpus_ImportsExecutesAndRoundTrips_WhenAvailable()
    {
        if (!Directory.Exists(SampleCorpusPath))
        {
            return;
        }

        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var viewerService = new ReportViewerSessionService();
        var serializer = new ReportRdlSerializer();

        foreach (var path in Directory.EnumerateFiles(SampleCorpusPath, "*.rdl").OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            var opened = await service.ImportRdlAsync(path);
            var workspace = Assert.IsType<ReportingStudioWorkspace>(opened.Workspace);
            Assert.Empty(opened.Diagnostics);

            var suppliedParameters = await ResolveSampleParametersAsync(viewerService, workspace.Source);
            var snapshot = await viewerService.ExecuteAsync(workspace.Source, suppliedParameters);

            Assert.NotNull(snapshot.ExecutionResult.MaterializedReport);
            Assert.NotNull(snapshot.ExecutionResult.Document);
            Assert.NotEmpty(snapshot.PreviewPages);
            Assert.Empty(snapshot.ExecutionResult.Diagnostics);

            var writeResult = serializer.Write(workspace.Source.ReportDefinition);
            Assert.Empty(writeResult.Diagnostics);

            var readResult = serializer.Read(writeResult.Xml);
            Assert.NotNull(readResult.ReportDefinition);
            Assert.Empty(readResult.Diagnostics);
        }
    }

    [Fact]
    public async Task SampleCorpus_InvoicePreservesNestedContainerLayout_WhenAvailable()
    {
        var invoicePath = Path.Combine(SampleCorpusPath, "Invoice.rdl");
        if (!File.Exists(invoicePath))
        {
            return;
        }

        var workspaceFactory = new ReportingStudioWorkspaceFactory();
        var service = CreateDocumentService(workspaceFactory);
        var viewerService = new ReportViewerSessionService();

        var opened = await service.ImportRdlAsync(invoicePath);
        var workspace = Assert.IsType<ReportingStudioWorkspace>(opened.Workspace);
        var suppliedParameters = await ResolveSampleParametersAsync(viewerService, workspace.Source);
        var snapshot = await viewerService.ExecuteAsync(workspace.Source, suppliedParameters);

        var document = Assert.IsType<Document>(snapshot.ExecutionResult.Document);
        Assert.Contains(document.Blocks, static block => block is TableBlock);
        Assert.Contains(EnumerateShapeInlines(document), static shape => shape.TextBox is { Blocks.Count: > 0 });
        Assert.Contains(EnumerateShapeTextBlocks(document), static block => block is TableBlock);
        Assert.Contains(
            EnumerateShapeTextParagraphs(document),
            static paragraph => paragraph.FloatingObjects.Count > 0);
    }

    private static ReportingStudioDocumentService CreateDocumentService(ReportingStudioWorkspaceFactory workspaceFactory)
    {
        return new ReportingStudioDocumentService(
            new ReportTemplateSerializer(),
            new ReportRdlSerializer(),
            workspaceFactory);
    }

    private static async Task<Dictionary<string, ReportParameterValue>> ResolveSampleParametersAsync(
        ReportViewerSessionService viewerService,
        ReportViewerSource source)
    {
        var supplied = new Dictionary<string, ReportParameterValue>(StringComparer.OrdinalIgnoreCase);
        var resolution = await viewerService.ResolveParametersAsync(source, supplied);
        for (var index = 0; index < resolution.Parameters.Count; index++)
        {
            var parameter = resolution.Parameters[index];
            if (parameter.ResolvedValue is not null && !parameter.ResolvedValue.IsNull)
            {
                supplied[parameter.Definition.Id] = parameter.ResolvedValue;
                continue;
            }

            supplied[parameter.Definition.Id] = parameter.Definition.DataType switch
            {
                ReportParameterDataType.Integer => ReportParameterValue.FromScalar(1),
                ReportParameterDataType.Number => ReportParameterValue.FromScalar(1d),
                ReportParameterDataType.Boolean => ReportParameterValue.FromScalar(true),
                ReportParameterDataType.DateTime => ReportParameterValue.FromScalar(new DateTime(2026, 3, 17, 12, 0, 0, DateTimeKind.Utc)),
                _ => ReportParameterValue.FromScalar("Sample")
            };
        }

        return supplied;
    }

    private static Task ExecuteAsync(ReactiveCommand<Unit, Unit> command)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? subscription = null;
        subscription = command.Execute().Subscribe(
            _ => { },
            ex =>
            {
                subscription?.Dispose();
                tcs.TrySetException(ex);
            },
            () =>
            {
                subscription?.Dispose();
                tcs.TrySetResult();
            });

        return tcs.Task;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static IEnumerable<ShapeInline> EnumerateShapeInlines(Document document)
    {
        foreach (var block in document.Blocks)
        {
            foreach (var shape in EnumerateShapeInlines(block))
            {
                yield return shape;
            }
        }
    }

    private static IEnumerable<Block> EnumerateShapeTextBlocks(Document document)
    {
        foreach (var shape in EnumerateShapeInlines(document))
        {
            if (shape.TextBox is null)
            {
                continue;
            }

            foreach (var block in shape.TextBox.Blocks)
            {
                yield return block;
            }
        }
    }

    private static IEnumerable<ParagraphBlock> EnumerateShapeTextParagraphs(Document document)
    {
        foreach (var block in EnumerateShapeTextBlocks(document))
        {
            if (block is ParagraphBlock paragraph)
            {
                yield return paragraph;
            }
        }
    }

    private static IEnumerable<ShapeInline> EnumerateShapeInlines(Block block)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                foreach (var inline in paragraph.Inlines)
                {
                    foreach (var shape in EnumerateShapeInlines(inline))
                    {
                        yield return shape;
                    }
                }

                foreach (var floating in paragraph.FloatingObjects)
                {
                    foreach (var shape in EnumerateShapeInlines(floating.Content))
                    {
                        yield return shape;
                    }
                }

                break;

            case TableBlock table:
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var paragraphInCell in cell.Paragraphs)
                        {
                            foreach (var shape in EnumerateShapeInlines(paragraphInCell))
                            {
                                yield return shape;
                            }
                        }
                    }
                }

                break;
        }
    }

    private static IEnumerable<ShapeInline> EnumerateShapeInlines(Inline inline)
    {
        if (inline is ShapeInline shape)
        {
            yield return shape;

            if (shape.TextBox is not null)
            {
                foreach (var block in shape.TextBox.Blocks)
                {
                    foreach (var nested in EnumerateShapeInlines(block))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }

    private static string ResolveSampleCorpusPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VibeOffice.slnx")))
            {
                return Path.Combine(directory.FullName, "external", "Reporting-Services", "PaginatedReportSamples");
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "external", "Reporting-Services", "PaginatedReportSamples");
    }

    private sealed class StubReportingStudioFilePickerService : IReportingStudioFilePickerService
    {
        public string? SaveTemplatePath { get; init; }

        public string? ExportRdlPath { get; init; }

        public ValueTask<string?> PickOpenTemplatePathAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<string?>(null);
        }

        public ValueTask<string?> PickSaveTemplatePathAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(SaveTemplatePath);
        }

        public ValueTask<string?> PickImportRdlPathAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<string?>(null);
        }

        public ValueTask<string?> PickExportRdlPathAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(ExportRdlPath);
        }
    }
}

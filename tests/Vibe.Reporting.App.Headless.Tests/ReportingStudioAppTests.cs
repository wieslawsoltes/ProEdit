using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Reactive;
using ReactiveUI;
using Vibe.Office.Reporting.Avalonia.Designer;
using Vibe.Office.Reporting.Avalonia.Viewer;
using Vibe.Office.Reporting.Rdl;
using Vibe.Office.Reporting.Serialization;
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

        Assert.True(window.GetVisualDescendants().OfType<TabControl>().Count() >= 2);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => string.Equals(button.Content as string, "Open Template", StringComparison.Ordinal));
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => string.Equals(text.Text, "VibeOffice Reporting Studio", StringComparison.Ordinal));
        Assert.NotNull(viewModel.ViewerViewModel.CurrentSnapshot);
        Assert.NotEmpty(viewModel.ViewerViewModel.Pages);

        window.Close();
    }

    private static ReportingStudioDocumentService CreateDocumentService(ReportingStudioWorkspaceFactory workspaceFactory)
    {
        return new ReportingStudioDocumentService(
            new ReportTemplateSerializer(),
            new ReportRdlSerializer(),
            workspaceFactory);
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

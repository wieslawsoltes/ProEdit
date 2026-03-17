using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using ReactiveUI.Avalonia;
using Vibe.Office.Reporting.Rdl;
using Vibe.Office.Reporting.Serialization;
using Vibe.Reporting.App.Services;
using Vibe.Reporting.App.ViewModels;

namespace Vibe.Reporting.App;

internal partial class App : Application
{
    public override void Initialize()
    {
        RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var startupPath = TryResolveStartupPath(desktop.Args);
            var window = new MainWindow();
            var pickerService = new AvaloniaReportingStudioFilePickerService(window);
            var workspaceFactory = new ReportingStudioWorkspaceFactory();
            var documentService = new ReportingStudioDocumentService(
                new ReportTemplateSerializer(),
                new ReportRdlSerializer(),
                workspaceFactory);

            var viewModel = new ReportingStudioViewModel(pickerService, documentService);
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            _ = viewModel.InitializeAsync(startupPath);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? TryResolveStartupPath(string[]? args)
    {
        if (args is not { Length: > 0 })
        {
            return null;
        }

        var candidate = args[0];
        return File.Exists(candidate) ? candidate : null;
    }
}

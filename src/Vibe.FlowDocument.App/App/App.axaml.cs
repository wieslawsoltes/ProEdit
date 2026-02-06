using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using ReactiveUI.Avalonia;
using Vibe.FlowDocument.App.Services;
using Vibe.FlowDocument.App.ViewModels;
using Vibe.Office.FlowDocument.IO;

namespace Vibe.FlowDocument.App;

public partial class App : Application
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
            var window = new MainWindow();
            var pickerService = new AvaloniaFlowDocumentFilePickerService(window);
            var conversionService = new FlowDocumentFileConversionService();
            window.DataContext = new FlowDocumentSampleViewModel(conversionService, pickerService);

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

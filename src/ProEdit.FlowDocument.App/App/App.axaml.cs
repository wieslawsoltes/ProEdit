using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using ReactiveUI.Avalonia;
using ProEdit.FlowDocument.App.Services;
using ProEdit.FlowDocument.App.ViewModels;
using ProEdit.FlowDocument.IO;

namespace ProEdit.FlowDocument.App;

public partial class App : Application
{
    public override void Initialize()
    {
        RxSchedulers.MainThreadScheduler = AvaloniaScheduler.Instance;
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

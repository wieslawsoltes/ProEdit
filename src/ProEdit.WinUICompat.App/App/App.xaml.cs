using Microsoft.UI.Xaml;
using ProEdit.FlowDocument.IO;
using ProEdit.WinUICompat.App.Services;
using ProEdit.WinUICompat.App.ViewModels;

namespace ProEdit.WinUICompat.App;

public sealed partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var pickerService = new UnoFlowDocumentFilePickerService();
        var conversionService = new FlowDocumentFileConversionService();
        var viewModel = new WinUICompatSampleViewModel(conversionService, pickerService);
        var mainWindow = new MainWindow();
        if (mainWindow.Content is FrameworkElement root)
        {
            root.DataContext = viewModel;
        }

        _window = mainWindow;

        _window.Activate();
    }
}

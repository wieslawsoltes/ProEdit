using Avalonia.Controls;

namespace Vibe.Office.Printing.Avalonia;

public sealed partial class PrintDialog : Window
{
    public PrintDialog()
    {
        InitializeComponent();
    }

    public PrintDialog(PrintDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

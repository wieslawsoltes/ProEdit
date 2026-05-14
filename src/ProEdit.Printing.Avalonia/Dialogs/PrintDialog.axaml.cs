using Avalonia.Controls;

namespace ProEdit.Printing.Avalonia;

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

using Avalonia.Controls;

namespace ProEdit.Word.Avalonia;

public partial class PdfImportDialog : Window
{
    public PdfImportDialog()
    {
        InitializeComponent();
    }

    public PdfImportDialog(PdfImportDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

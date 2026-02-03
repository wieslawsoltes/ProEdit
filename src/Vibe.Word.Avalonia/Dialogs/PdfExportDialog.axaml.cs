using Avalonia.Controls;

namespace Vibe.Word.Avalonia;

public partial class PdfExportDialog : Window
{
    public PdfExportDialog()
    {
        InitializeComponent();
    }

    public PdfExportDialog(PdfExportDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

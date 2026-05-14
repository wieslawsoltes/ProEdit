using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ProEdit.Word.Avalonia;

public partial class MacroDiagnosticsDialog : Window
{
    private readonly TextBox _detailsText;

    public MacroDiagnosticsDialog()
    {
        InitializeComponent();
        _detailsText = this.FindControl<TextBox>("DetailsText")!;
    }

    public MacroDiagnosticsDialog(string title, string details)
        : this()
    {
        Title = title;
        _detailsText.Text = details ?? string.Empty;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

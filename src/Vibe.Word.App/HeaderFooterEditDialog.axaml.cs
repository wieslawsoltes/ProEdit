using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vibe.Word.App;

public partial class HeaderFooterEditDialog : Window
{
    private readonly TextBox _textBox;

    public HeaderFooterEditDialog()
        : this("Edit Header/Footer", string.Empty)
    {
    }

    public HeaderFooterEditDialog(string title, string? text)
    {
        InitializeComponent();
        Title = title;
        _textBox = this.FindControl<TextBox>("HeaderFooterTextBox")!;
        _textBox.Text = text ?? string.Empty;
        _textBox.Focus();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        Close(_textBox.Text ?? string.Empty);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

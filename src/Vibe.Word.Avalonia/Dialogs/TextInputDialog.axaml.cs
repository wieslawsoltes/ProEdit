using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vibe.Word.Avalonia;

public partial class TextInputDialog : Window
{
    private readonly TextBlock _promptText;
    private readonly TextBox _inputText;

    public TextInputDialog()
        : this("Input", "Enter value:", null)
    {
    }

    public TextInputDialog(string title, string prompt, string? initialValue)
    {
        InitializeComponent();
        Title = title;
        _promptText = this.FindControl<TextBlock>("PromptText")!;
        _inputText = this.FindControl<TextBox>("InputText")!;
        _promptText.Text = prompt;
        _inputText.Text = initialValue ?? string.Empty;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _inputText.Focus();
        _inputText.SelectAll();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(_inputText.Text);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

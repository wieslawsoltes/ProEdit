using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vibe.Word.App;

public partial class MessageDialog : Window
{
    private readonly TextBox _messageText;

    public MessageDialog()
    {
        InitializeComponent();
        _messageText = this.FindControl<TextBox>("MessageText")!;
    }

    public MessageDialog(string title, string message)
        : this()
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Message" : title;
        _messageText.Text = message ?? string.Empty;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

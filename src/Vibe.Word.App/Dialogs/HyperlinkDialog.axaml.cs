using Avalonia.Controls;
using Avalonia.Interactivity;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public partial class HyperlinkDialog : Window
{
    private readonly TextBox _displayTextBox;
    private readonly TextBox _addressTextBox;
    private readonly TextBox _tooltipTextBox;
    private readonly TextBox _anchorTextBox;
    private readonly TextBlock _validationText;
    private readonly Button _insertButton;

    public HyperlinkDialog()
        : this(null, null)
    {
    }

    public HyperlinkDialog(string? displayText = null, string? address = null)
    {
        InitializeComponent();
        _displayTextBox = this.FindControl<TextBox>("DisplayTextBox")!;
        _addressTextBox = this.FindControl<TextBox>("AddressTextBox")!;
        _tooltipTextBox = this.FindControl<TextBox>("TooltipTextBox")!;
        _anchorTextBox = this.FindControl<TextBox>("AnchorTextBox")!;
        _validationText = this.FindControl<TextBlock>("ValidationText")!;
        _insertButton = this.FindControl<Button>("InsertButton")!;
        _displayTextBox.Text = displayText ?? string.Empty;
        _addressTextBox.Text = address ?? string.Empty;
        UpdateValidation();
        _displayTextBox.Focus();
    }

    private void OnFieldChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateValidation();
    }

    private void OnInsertClick(object? sender, RoutedEventArgs e)
    {
        var request = new EditorHyperlinkInsertRequest(
            NormalizeValue(_addressTextBox.Text),
            NormalizeValue(_anchorTextBox.Text),
            NormalizeValue(_displayTextBox.Text),
            NormalizeValue(_tooltipTextBox.Text));
        Close(request);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateValidation()
    {
        var address = _addressTextBox.Text;
        var anchor = _anchorTextBox.Text;
        var isValid = IsValidHyperlink(address, anchor, out var message);
        _insertButton.IsEnabled = isValid;
        _validationText.IsVisible = !isValid;
        _validationText.Text = message;
    }

    private static bool IsValidHyperlink(string? address, string? anchor, out string message)
    {
        var trimmedAddress = NormalizeValue(address);
        var trimmedAnchor = NormalizeValue(anchor);

        if (string.IsNullOrWhiteSpace(trimmedAddress) && string.IsNullOrWhiteSpace(trimmedAnchor))
        {
            message = "Enter a web address or bookmark.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(trimmedAddress))
        {
            message = string.Empty;
            return true;
        }

        if (trimmedAddress.AsSpan().IndexOfAny(" \t\r\n".AsSpan()) >= 0)
        {
            message = "Address cannot contain spaces.";
            return false;
        }

        if (Uri.TryCreate(trimmedAddress, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https" or "mailto" or "file" or "ftp")
            {
                message = string.Empty;
                return true;
            }

            message = $"Unsupported URI scheme: {uri.Scheme}.";
            return false;
        }

        if (Uri.TryCreate(trimmedAddress, UriKind.Relative, out _))
        {
            message = string.Empty;
            return true;
        }

        message = "Enter a valid address.";
        return false;
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public enum FindReplaceDialogMode
{
    Find,
    Replace
}

public partial class FindReplaceDialog : Window
{
    private readonly TextBox _findTextBox;
    private readonly TextBox _replaceTextBox;
    private readonly TextBlock _replaceLabel;
    private readonly CheckBox _matchCaseCheck;
    private readonly CheckBox _wholeWordCheck;
    private readonly CheckBox _wrapCheck;
    private readonly Button _replaceButton;
    private readonly Button _replaceAllButton;

    public event EventHandler<EditorFindQuery>? FindNextRequested;
    public event EventHandler<EditorReplaceQuery>? ReplaceRequested;
    public event EventHandler<EditorReplaceQuery>? ReplaceAllRequested;

    public FindReplaceDialog()
    {
        InitializeComponent();
        _findTextBox = this.FindControl<TextBox>("FindTextBox")!;
        _replaceTextBox = this.FindControl<TextBox>("ReplaceTextBox")!;
        _replaceLabel = this.FindControl<TextBlock>("ReplaceLabel")!;
        _matchCaseCheck = this.FindControl<CheckBox>("MatchCaseCheck")!;
        _wholeWordCheck = this.FindControl<CheckBox>("WholeWordCheck")!;
        _wrapCheck = this.FindControl<CheckBox>("WrapCheck")!;
        _replaceButton = this.FindControl<Button>("ReplaceButton")!;
        _replaceAllButton = this.FindControl<Button>("ReplaceAllButton")!;
        _findTextBox.Focus();
    }

    public void SetMode(FindReplaceDialogMode mode)
    {
        var showReplace = mode == FindReplaceDialogMode.Replace;
        _replaceLabel.IsVisible = showReplace;
        _replaceTextBox.IsVisible = showReplace;
        _replaceButton.IsVisible = showReplace;
        _replaceAllButton.IsVisible = showReplace;
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e)
    {
        FindNextRequested?.Invoke(this, BuildFindQuery());
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
    {
        ReplaceRequested?.Invoke(this, BuildReplaceQuery());
    }

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
    {
        ReplaceAllRequested?.Invoke(this, BuildReplaceQuery());
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private EditorFindQuery BuildFindQuery()
    {
        var text = _findTextBox.Text ?? string.Empty;
        return new EditorFindQuery(
            text,
            _matchCaseCheck.IsChecked ?? false,
            _wholeWordCheck.IsChecked ?? false,
            _wrapCheck.IsChecked ?? true);
    }

    private EditorReplaceQuery BuildReplaceQuery()
    {
        var text = _findTextBox.Text ?? string.Empty;
        var replacement = _replaceTextBox.Text ?? string.Empty;
        return new EditorReplaceQuery(
            text,
            replacement,
            _matchCaseCheck.IsChecked ?? false,
            _wholeWordCheck.IsChecked ?? false,
            _wrapCheck.IsChecked ?? true);
    }
}

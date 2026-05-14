using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ProEdit.Word.Avalonia;

public partial class StylePaneOptionsDialog : Window
{
    private readonly ComboBox _filterCombo;
    private readonly ComboBox _sortCombo;
    private readonly ComboBox _typeCombo;
    private readonly CheckBox _showPreviewCheckBox;
    private readonly CheckBox _showHiddenCheckBox;
    private StylePaneOptions _options;

    public StylePaneOptionsDialog(StylePaneOptions options)
    {
        InitializeComponent();
        _filterCombo = this.FindControl<ComboBox>("FilterCombo")!;
        _sortCombo = this.FindControl<ComboBox>("SortCombo")!;
        _typeCombo = this.FindControl<ComboBox>("TypeCombo")!;
        _showPreviewCheckBox = this.FindControl<CheckBox>("ShowPreviewCheckBox")!;
        _showHiddenCheckBox = this.FindControl<CheckBox>("ShowHiddenCheckBox")!;
        _options = options.Clone();

        if (this.FindControl<Button>("OkButton") is { } okButton)
        {
            okButton.Click += OnOkClick;
        }

        if (this.FindControl<Button>("CancelButton") is { } cancelButton)
        {
            cancelButton.Click += OnCancelClick;
        }

        SetState(_options);
    }

    private void SetState(StylePaneOptions options)
    {
        _filterCombo.SelectedIndex = (int)options.FilterMode;
        _sortCombo.SelectedIndex = (int)options.SortMode;
        _typeCombo.SelectedIndex = (int)options.TypeFilter;
        _showPreviewCheckBox.IsChecked = options.ShowPreview;
        _showHiddenCheckBox.IsChecked = options.ShowHidden;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var result = new StylePaneOptions
        {
            FilterMode = _filterCombo.SelectedIndex >= 0
                ? (StylePaneFilterMode)_filterCombo.SelectedIndex
                : StylePaneFilterMode.All,
            SortMode = _sortCombo.SelectedIndex >= 0
                ? (StylePaneSortMode)_sortCombo.SelectedIndex
                : StylePaneSortMode.Alphabetical,
            TypeFilter = _typeCombo.SelectedIndex >= 0
                ? (StylePaneTypeFilter)_typeCombo.SelectedIndex
                : StylePaneTypeFilter.All,
            ShowPreview = _showPreviewCheckBox.IsChecked == true,
            ShowHidden = _showHiddenCheckBox.IsChecked == true
        };

        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}

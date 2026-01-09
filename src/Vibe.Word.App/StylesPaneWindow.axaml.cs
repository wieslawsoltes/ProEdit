using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public partial class StylesPaneWindow : Window
{
    private IStyleService? _styleService;
    private TextBlock? _paneTitle;
    private ListBox? _stylesList;

    public StylesPaneWindow()
    {
        InitializeComponent();
        InitializeControls();
    }

    public StylesPaneWindow(IStyleService styleService)
        : this()
    {
        SetService(styleService);
    }

    private void InitializeControls()
    {
        _paneTitle = this.FindControl<TextBlock>("PaneTitle");
        _stylesList = this.FindControl<ListBox>("StylesList");

        if (this.FindControl<Button>("ApplyButton") is { } applyButton)
        {
            applyButton.Click += OnApplyClick;
        }

        if (this.FindControl<Button>("CloseButton") is { } closeButton)
        {
            closeButton.Click += (_, _) => Close();
        }

        if (_stylesList is not null)
        {
            _stylesList.DoubleTapped += OnStylesDoubleTapped;
        }
    }

    public void SetMode(bool manageMode)
    {
        var title = manageMode ? "Manage Styles" : "Styles";
        Title = title;
        if (_paneTitle is not null)
        {
            _paneTitle.Text = title;
        }
    }

    public void SetService(IStyleService styleService)
    {
        _styleService = styleService ?? throw new ArgumentNullException(nameof(styleService));
        RefreshStyles();
    }

    private void RefreshStyles()
    {
        if (_stylesList is null || _styleService is null)
        {
            return;
        }

        _stylesList.ItemsSource = _styleService.GetParagraphStyles();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        ApplySelectedStyle();
    }

    private void OnStylesDoubleTapped(object? sender, TappedEventArgs e)
    {
        ApplySelectedStyle();
    }

    private void ApplySelectedStyle()
    {
        if (_styleService is null || _stylesList?.SelectedItem is not EditorParagraphStyleInfo style)
        {
            return;
        }

        _styleService.ApplyParagraphStyle(style.Id);
    }
}

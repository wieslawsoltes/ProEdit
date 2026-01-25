using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Word.App;

public partial class StylesPaneWindow : Window
{
    private IStyleService? _styleService;
    private TextBlock? _paneTitle;
    private ListBox? _stylesList;
    private TextBox? _searchBox;
    private ComboBox? _filterCombo;
    private TextBlock? _previewText;
    private TextBlock? _detailsText;
    private TextBlock? _directFormattingText;
    private Border? _managePanel;
    private TextBox? _styleNameBox;
    private ComboBox? _basedOnCombo;
    private ComboBox? _nextStyleCombo;
    private Button? _setDefaultButton;
    private Button? _updateStyleButton;
    private IReadOnlyList<EditorParagraphStyleInfo> _allStyles = Array.Empty<EditorParagraphStyleInfo>();
    private HashSet<string> _stylesInUse = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _styleNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _manageMode;

    public StylesPaneWindow()
    {
        InitializeComponent();
        InitializeControls();
        Activated += (_, _) => RefreshStyles();
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
        _searchBox = this.FindControl<TextBox>("StyleSearchBox");
        _filterCombo = this.FindControl<ComboBox>("StyleFilterCombo");
        _previewText = this.FindControl<TextBlock>("StylePreviewText");
        _detailsText = this.FindControl<TextBlock>("StyleDetailsText");
        _directFormattingText = this.FindControl<TextBlock>("StyleDirectFormattingText");
        _managePanel = this.FindControl<Border>("StyleManagePanel");
        _styleNameBox = this.FindControl<TextBox>("StyleNameBox");
        _basedOnCombo = this.FindControl<ComboBox>("StyleBasedOnCombo");
        _nextStyleCombo = this.FindControl<ComboBox>("StyleNextCombo");
        _setDefaultButton = this.FindControl<Button>("SetDefaultButton");
        _updateStyleButton = this.FindControl<Button>("UpdateStyleButton");

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
            _stylesList.SelectionChanged += OnStyleSelectionChanged;
            _stylesList.KeyDown += OnStylesKeyDown;
        }

        if (_searchBox is not null)
        {
            _searchBox.TextChanged += (_, _) => RefreshStyles();
        }

        if (_filterCombo is not null)
        {
            _filterCombo.SelectionChanged += (_, _) => RefreshStyles();
            if (_filterCombo.SelectedIndex < 0)
            {
                _filterCombo.SelectedIndex = 0;
            }
        }

        if (_setDefaultButton is not null)
        {
            _setDefaultButton.Click += OnSetDefaultClick;
        }

        if (_updateStyleButton is not null)
        {
            _updateStyleButton.Click += OnUpdateStyleClick;
        }
    }

    public void SetMode(bool manageMode)
    {
        _manageMode = manageMode;
        var title = manageMode ? "Manage Styles" : "Styles";
        Title = title;
        if (_paneTitle is not null)
        {
            _paneTitle.Text = title;
        }

        if (_managePanel is not null)
        {
            _managePanel.IsVisible = manageMode;
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

        var selectedId = _stylesList.SelectedItem is EditorParagraphStyleInfo selected
            ? selected.Id
            : null;

        _allStyles = _styleService.GetParagraphStyles();
        _stylesInUse = new HashSet<string>(_styleService.GetParagraphStylesInUse(), StringComparer.OrdinalIgnoreCase);
        _styleNames = BuildStyleNameMap(_allStyles);

        var search = _searchBox?.Text;
        var filterInUse = _filterCombo?.SelectedIndex == 1;
        var filtered = new List<EditorParagraphStyleInfo>(_allStyles.Count);
        foreach (var style in _allStyles)
        {
            if (filterInUse && !_stylesInUse.Contains(style.Id))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(search)
                && style.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            filtered.Add(style);
        }

        _stylesList.ItemsSource = filtered;

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var restoredIndex = filtered.FindIndex(info => string.Equals(info.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (restoredIndex >= 0)
            {
                _stylesList.SelectedIndex = restoredIndex;
            }
        }

        if (_stylesList.SelectedIndex < 0 && filtered.Count > 0)
        {
            _stylesList.SelectedIndex = 0;
        }

        UpdateStyleDetails();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        ApplySelectedStyle();
    }

    private void OnStylesDoubleTapped(object? sender, TappedEventArgs e)
    {
        ApplySelectedStyle();
    }

    private void OnStylesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplySelectedStyle();
        e.Handled = true;
    }

    private void OnStyleSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateStyleDetails();
    }

    private void ApplySelectedStyle()
    {
        if (_styleService is null || _stylesList?.SelectedItem is not EditorParagraphStyleInfo style)
        {
            return;
        }

        _styleService.ApplyParagraphStyle(style.Id);
    }

    private void UpdateStyleDetails()
    {
        if (_styleService is null)
        {
            return;
        }

        if (_stylesList?.SelectedItem is not EditorParagraphStyleInfo style)
        {
            ApplyPreviewStyle(null);
            SetDetailsText(string.Empty, string.Empty);
            SetManageControls(null, false);
            return;
        }

        var definition = _styleService.GetParagraphStyle(style.Id);
        var preview = _styleService.GetParagraphStylePreview(style.Id);
        ApplyPreviewStyle(preview);

        var basedOnName = ResolveStyleName(definition?.BasedOnId);
        var nextName = ResolveStyleName(definition?.NextStyleId);
        var inUse = _stylesInUse.Contains(style.Id);
        var detailText = $"Based on: {basedOnName}\nNext style: {nextName}\nDefault: {(style.IsDefault ? "Yes" : "No")}  In use: {(inUse ? "Yes" : "No")}";

        var directFormatting = _styleService.GetDirectFormattingInfo();
        var directParts = new List<string>(2);
        if (directFormatting.HasParagraphFormatting)
        {
            directParts.Add("Paragraph");
        }

        if (directFormatting.HasCharacterFormatting)
        {
            directParts.Add("Character");
        }

        var directText = directParts.Count == 0
            ? "Direct formatting: None"
            : $"Direct formatting: {string.Join(", ", directParts)}";

        SetDetailsText(detailText, directText);
        SetManageControls(definition, style.IsDefault);
    }

    private void SetDetailsText(string details, string direct)
    {
        if (_detailsText is not null)
        {
            _detailsText.Text = details;
        }

        if (_directFormattingText is not null)
        {
            _directFormattingText.Text = direct;
        }
    }

    private void SetManageControls(ParagraphStyleDefinition? definition, bool isDefault)
    {
        if (!_manageMode || _styleService is null || definition is null)
        {
            SetManageEnabled(false);
            return;
        }

        var isLocked = definition.Locked == true;
        SetManageEnabled(!isLocked);

        if (_styleNameBox is not null)
        {
            _styleNameBox.Text = definition.Name ?? definition.Id;
        }

        if (_basedOnCombo is not null)
        {
            _basedOnCombo.ItemsSource = BuildStyleComboItems();
            SelectStyleComboItem(_basedOnCombo, definition.BasedOnId);
        }

        if (_nextStyleCombo is not null)
        {
            _nextStyleCombo.ItemsSource = BuildStyleComboItems();
            SelectStyleComboItem(_nextStyleCombo, definition.NextStyleId);
        }

        if (_setDefaultButton is not null)
        {
            _setDefaultButton.IsEnabled = !isLocked && !isDefault;
        }
    }

    private void SetManageEnabled(bool enabled)
    {
        if (_styleNameBox is not null)
        {
            _styleNameBox.IsEnabled = enabled;
        }

        if (_basedOnCombo is not null)
        {
            _basedOnCombo.IsEnabled = enabled;
        }

        if (_nextStyleCombo is not null)
        {
            _nextStyleCombo.IsEnabled = enabled;
        }

        if (_updateStyleButton is not null)
        {
            _updateStyleButton.IsEnabled = enabled;
        }
    }

    private void OnUpdateStyleClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService is null || _stylesList?.SelectedItem is not EditorParagraphStyleInfo style)
        {
            return;
        }

        var name = _styleNameBox?.Text ?? string.Empty;
        var basedOnId = (_basedOnCombo?.SelectedItem as StyleComboItem)?.Id;
        var nextStyleId = (_nextStyleCombo?.SelectedItem as StyleComboItem)?.Id;

        var changed = false;
        if (!string.IsNullOrWhiteSpace(name))
        {
            changed |= _styleService.RenameParagraphStyle(style.Id, name);
        }

        changed |= _styleService.SetParagraphStyleBasedOn(style.Id, basedOnId);
        changed |= _styleService.SetParagraphStyleNext(style.Id, nextStyleId);

        if (changed)
        {
            RefreshStyles();
        }
    }

    private void OnSetDefaultClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService is null || _stylesList?.SelectedItem is not EditorParagraphStyleInfo style)
        {
            return;
        }

        if (_styleService.SetDefaultParagraphStyle(style.Id))
        {
            RefreshStyles();
        }
    }

    private List<StyleComboItem> BuildStyleComboItems()
    {
        var items = new List<StyleComboItem>(_allStyles.Count + 1)
        {
            new StyleComboItem(null, "None")
        };

        foreach (var style in _allStyles)
        {
            items.Add(new StyleComboItem(style.Id, style.Name));
        }

        return items;
    }

    private static void SelectStyleComboItem(ComboBox combo, string? styleId)
    {
        if (combo.ItemsSource is not IEnumerable<StyleComboItem> items)
        {
            return;
        }

        foreach (var item in items)
        {
            if (string.Equals(item.Id, styleId, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private void ApplyPreviewStyle(TextStyle? style)
    {
        if (_previewText is null)
        {
            return;
        }

        if (style is null)
        {
            _previewText.FontFamily = FontFamily.Default;
            _previewText.FontSize = 14;
            _previewText.FontWeight = FontWeight.Normal;
            _previewText.FontStyle = FontStyle.Normal;
            _previewText.Foreground = Brushes.Black;
            _previewText.TextDecorations = null;
            return;
        }

        _previewText.FontFamily = new FontFamily(style.FontFamily);
        _previewText.FontSize = Math.Max(6, style.FontSize);
        _previewText.FontWeight = style.FontWeight == DocFontWeight.Bold ? FontWeight.Bold : FontWeight.Normal;
        _previewText.FontStyle = style.FontStyle == DocFontStyle.Italic ? FontStyle.Italic : FontStyle.Normal;
        _previewText.Foreground = new SolidColorBrush(ToAvaloniaColor(style.Color));
        _previewText.TextDecorations = BuildTextDecorations(style);
    }

    private static TextDecorationCollection? BuildTextDecorations(TextStyle style)
    {
        var hasUnderline = style.Underline || style.UnderlineStyle != DocUnderlineStyle.None;
        var hasStrike = style.Strikethrough;
        if (!hasUnderline && !hasStrike)
        {
            return null;
        }

        var decorations = new TextDecorationCollection();
        if (hasUnderline)
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        }

        if (hasStrike)
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        }

        return decorations;
    }

    private static Color ToAvaloniaColor(DocColor color)
    {
        return new Color(color.A, color.R, color.G, color.B);
    }

    private string ResolveStyleName(string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return "None";
        }

        if (_styleNames.TryGetValue(styleId, out var name))
        {
            return name;
        }

        return styleId;
    }

    private static Dictionary<string, string> BuildStyleNameMap(IReadOnlyList<EditorParagraphStyleInfo> styles)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in styles)
        {
            map[style.Id] = style.Name;
        }

        return map;
    }

    private sealed record StyleComboItem(string? Id, string Name)
    {
        public override string ToString() => Name;
    }
}

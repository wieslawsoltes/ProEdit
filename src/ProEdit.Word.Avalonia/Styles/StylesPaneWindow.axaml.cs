using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ProEdit.Documents;
using ProEdit.Editing;
using ProEdit.Primitives;

namespace ProEdit.Word.Avalonia;

public partial class StylesPaneWindow : Window
{
    private static StylePaneOptions _sharedOptions = new();

    private IStyleManagerService? _styleService;
    private IFontService? _fontService;
    private Action? _openManageDialog;
    private TextBlock? _paneTitle;
    private ListBox? _stylesList;
    private TextBox? _searchBox;
    private ComboBox? _filterCombo;
    private ComboBox? _typeCombo;
    private TextBlock? _previewText;
    private TextBlock? _detailsText;
    private TextBlock? _directFormattingText;
    private Border? _previewPanel;
    private IReadOnlyList<EditorStyleInfo> _allStyles = Array.Empty<EditorStyleInfo>();
    private Dictionary<string, string> _styleNames = new(StringComparer.OrdinalIgnoreCase);
    private StylePaneOptions _options = new();

    public StylesPaneWindow()
    {
        InitializeComponent();
        _options = _sharedOptions.Clone();
        InitializeControls();
        Activated += (_, _) => RefreshStyles();
    }

    public StylesPaneWindow(IStyleManagerService styleService, IFontService? fontService = null, Action? openManageDialog = null)
        : this()
    {
        _openManageDialog = openManageDialog;
        SetServices(styleService, fontService);
    }

    private void InitializeControls()
    {
        _paneTitle = this.FindControl<TextBlock>("PaneTitle");
        _stylesList = this.FindControl<ListBox>("StylesList");
        _searchBox = this.FindControl<TextBox>("StyleSearchBox");
        _filterCombo = this.FindControl<ComboBox>("StyleFilterCombo");
        _typeCombo = this.FindControl<ComboBox>("StyleTypeCombo");
        _previewText = this.FindControl<TextBlock>("StylePreviewText");
        _detailsText = this.FindControl<TextBlock>("StyleDetailsText");
        _directFormattingText = this.FindControl<TextBlock>("StyleDirectFormattingText");
        _previewPanel = this.FindControl<Border>("StylePreviewPanel");

        if (this.FindControl<Button>("ApplyButton") is { } applyButton)
        {
            applyButton.Click += OnApplyClick;
        }

        if (this.FindControl<Button>("CloseButton") is { } closeButton)
        {
            closeButton.Click += (_, _) => Close();
        }

        if (this.FindControl<Button>("NewStyleButton") is { } newStyleButton)
        {
            newStyleButton.Click += OnNewStyleClick;
        }

        if (this.FindControl<Button>("InspectorButton") is { } inspectorButton)
        {
            inspectorButton.Click += OnInspectorClick;
        }

        if (this.FindControl<Button>("ManageButton") is { } manageButton)
        {
            manageButton.Click += OnManageClick;
        }

        if (this.FindControl<Button>("OrganizerButton") is { } organizerButton)
        {
            organizerButton.Click += OnOrganizerClick;
        }

        if (this.FindControl<Button>("OptionsButton") is { } optionsButton)
        {
            optionsButton.Click += OnOptionsClick;
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
                _filterCombo.SelectedIndex = (int)_options.FilterMode;
            }
        }

        if (_typeCombo is not null)
        {
            _typeCombo.SelectionChanged += (_, _) => RefreshStyles();
            if (_typeCombo.SelectedIndex < 0)
            {
                _typeCombo.SelectedIndex = (int)_options.TypeFilter;
            }
        }

        ApplyOptions();
    }

    public void SetServices(IStyleManagerService styleService, IFontService? fontService)
    {
        if (_styleService is not null)
        {
            _styleService.StylesChanged -= OnStylesChanged;
        }

        _styleService = styleService ?? throw new ArgumentNullException(nameof(styleService));
        _styleService.StylesChanged += OnStylesChanged;
        _fontService = fontService;
        RefreshStyles();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_styleService is not null)
        {
            _styleService.StylesChanged -= OnStylesChanged;
        }

        base.OnClosed(e);
    }

    private void RefreshStyles()
    {
        if (_stylesList is null || _styleService is null)
        {
            return;
        }

        var selectedId = _stylesList.SelectedItem is EditorStyleInfo selected
            ? selected.Id
            : null;

        if (_filterCombo is not null)
        {
            _options.FilterMode = _filterCombo.SelectedIndex >= 0
                ? (StylePaneFilterMode)_filterCombo.SelectedIndex
                : StylePaneFilterMode.All;
        }

        if (_typeCombo is not null)
        {
            _options.TypeFilter = _typeCombo.SelectedIndex >= 0
                ? (StylePaneTypeFilter)_typeCombo.SelectedIndex
                : StylePaneTypeFilter.All;
        }

        _allStyles = _styleService.GetStyles();
        _styleNames = BuildStyleNameMap(_allStyles);

        var search = _searchBox?.Text;
        var filtered = new List<EditorStyleInfo>(_allStyles.Count);
        foreach (var style in _allStyles)
        {
            if (!_options.Includes(style.Type))
            {
                continue;
            }

            if (!_options.ShowHidden)
            {
                if (style.IsHidden && !(style.IsInUse && style.UnhideWhenUsed))
                {
                    continue;
                }

                if (style.IsSemiHidden && !style.IsInUse)
                {
                    continue;
                }
            }

            if (!MatchesFilter(style, _options.FilterMode))
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

        SortStyles(filtered, _options.SortMode);
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
        ApplyOptions();
    }

    private void OnStylesChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshStyles);
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

    private void OnStyleSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateStyleDetails();
    }

    private void ApplySelectedStyle()
    {
        if (_styleService is null || _stylesList?.SelectedItem is not EditorStyleInfo style)
        {
            return;
        }

        _styleService.ApplyStyle(style.Type, style.Id);
    }

    private void UpdateStyleDetails()
    {
        if (_styleService is null)
        {
            return;
        }

        if (_stylesList?.SelectedItem is not EditorStyleInfo style)
        {
            ApplyPreviewStyle(null);
            SetDetailsText(string.Empty, string.Empty);
            return;
        }

        var definition = GetStyleDefinition(style);
        var preview = _styleService.GetStylePreview(style.Type, style.Id);
        ApplyPreviewStyle(preview, style.Type == EditorStyleType.Table);

        var basedOnName = ResolveStyleName(definition?.BasedOnId);
        var nextName = ResolveStyleName(definition?.NextStyleId);
        var detailText = string.Join(
            "\n",
            $"Type: {style.Type}",
            $"Based on: {basedOnName}",
            $"Next style: {nextName}",
            $"Default: {(style.IsDefault ? "Yes" : "No")}",
            $"In use: {(style.IsInUse ? "Yes" : "No")}",
            $"Quick style: {(style.IsQuickStyle ? "Yes" : "No")}",
            $"Hidden: {(style.IsHidden ? "Yes" : "No")}");

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

    private async void OnNewStyleClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService is null)
        {
            return;
        }

        var dialog = new StyleEditorDialog(
            new StyleEditorState(EditorStyleType.Paragraph, string.Empty, null, null, null, false, false, null, null, null, null, null),
            _styleService,
            _fontService);
        var result = await dialog.ShowDialog<StyleEditorResult?>(this);
        if (result is not StyleEditorResult created)
        {
            return;
        }

        var options = new EditorStyleCreateOptions(
            created.Type,
            created.Name,
            created.BasedOnId,
            created.NextStyleId,
            created.LinkedStyleId,
            created.QuickStyle,
            created.AutoRedefine,
            created.RunProperties,
            created.ParagraphProperties,
            created.TableProperties,
            created.TableCellProperties,
            created.StyleId);

        if (_styleService.CreateStyle(options))
        {
            RefreshStyles();
        }
    }

    private void OnInspectorClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService is null)
        {
            return;
        }

        var dialog = new StyleInspectorDialog(_styleService);
        dialog.ShowDialog(this);
    }

    private void OnManageClick(object? sender, RoutedEventArgs e)
    {
        if (_openManageDialog is not null)
        {
            _openManageDialog.Invoke();
            return;
        }

        if (_styleService is null)
        {
            return;
        }

        var dialog = new ManageStylesDialog(_styleService, _fontService);
        dialog.ShowDialog(this);
    }

    private void OnOrganizerClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService is null)
        {
            return;
        }

        var dialog = new StyleOrganizerDialog(_styleService);
        dialog.ShowDialog(this);
    }

    private async void OnOptionsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new StylePaneOptionsDialog(_options);
        var result = await dialog.ShowDialog<StylePaneOptions?>(this);
        if (result is null)
        {
            return;
        }

        _options = result;
        _sharedOptions = result.Clone();
        SyncFilterControls();
        ApplyOptions();
        RefreshStyles();
    }

    private void ApplyOptions()
    {
        if (_previewPanel is not null)
        {
            _previewPanel.IsVisible = _options.ShowPreview;
        }
    }

    private void SyncFilterControls()
    {
        if (_filterCombo is not null)
        {
            _filterCombo.SelectedIndex = (int)_options.FilterMode;
        }

        if (_typeCombo is not null)
        {
            _typeCombo.SelectedIndex = (int)_options.TypeFilter;
        }
    }

    private StyleDefinitionInfo? GetStyleDefinition(EditorStyleInfo style)
    {
        return style.Type switch
        {
            EditorStyleType.Paragraph => Wrap(_styleService?.GetParagraphStyleDefinition(style.Id)),
            EditorStyleType.Character => Wrap(_styleService?.GetCharacterStyleDefinition(style.Id)),
            EditorStyleType.Table => Wrap(_styleService?.GetTableStyleDefinition(style.Id)),
            _ => null
        };
    }

    private static StyleDefinitionInfo? Wrap(ParagraphStyleDefinition? definition)
    {
        return definition is null
            ? null
            : new StyleDefinitionInfo(definition.BasedOnId, definition.NextStyleId);
    }

    private static StyleDefinitionInfo? Wrap(CharacterStyleDefinition? definition)
    {
        return definition is null
            ? null
            : new StyleDefinitionInfo(definition.BasedOnId, definition.NextStyleId);
    }

    private static StyleDefinitionInfo? Wrap(TableStyleDefinition? definition)
    {
        return definition is null
            ? null
            : new StyleDefinitionInfo(definition.BasedOnId, definition.NextStyleId);
    }

    private void ApplyPreviewStyle(TextStyle? style, bool isTable = false)
    {
        if (_previewText is null)
        {
            return;
        }

        _previewText.Text = isTable ? "Table" : "AaBbCcDd";
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

    private static Dictionary<string, string> BuildStyleNameMap(IReadOnlyList<EditorStyleInfo> styles)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in styles)
        {
            map[style.Id] = style.Name;
        }

        return map;
    }

    private static bool MatchesFilter(EditorStyleInfo style, StylePaneFilterMode filter)
    {
        return filter switch
        {
            StylePaneFilterMode.InUse => style.IsInUse,
            StylePaneFilterMode.QuickStyles => style.IsQuickStyle,
            StylePaneFilterMode.Recommended => style.UiPriority.HasValue || style.IsQuickStyle,
            _ => true
        };
    }

    private static void SortStyles(List<EditorStyleInfo> styles, StylePaneSortMode mode)
    {
        styles.Sort((left, right) => CompareStyleInfo(left, right, mode));
    }

    private static int CompareStyleInfo(EditorStyleInfo left, EditorStyleInfo right, StylePaneSortMode mode)
    {
        var leftPriority = left.UiPriority ?? int.MaxValue;
        var rightPriority = right.UiPriority ?? int.MaxValue;
        return mode switch
        {
            StylePaneSortMode.ByPriority => leftPriority != rightPriority
                ? leftPriority.CompareTo(rightPriority)
                : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase),
            _ => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        };
    }

    private readonly record struct StyleDefinitionInfo(string? BasedOnId, string? NextStyleId);
}

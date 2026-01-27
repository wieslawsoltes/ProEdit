using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Vibe.Office.Documents;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

public partial class ManageStylesDialog : Window
{
    private IStyleManagerService? _styleService;
    private IFontService? _fontService;
    private ListBox? _stylesList;
    private TextBox? _searchBox;
    private ComboBox? _typeCombo;
    private TextBox? _styleNameBox;
    private TextBlock? _styleTypeText;
    private ComboBox? _basedOnCombo;
    private ComboBox? _nextStyleCombo;
    private TextBox? _priorityBox;
    private CheckBox? _quickStyleCheckBox;
    private CheckBox? _hiddenCheckBox;
    private CheckBox? _semiHiddenCheckBox;
    private CheckBox? _unhideWhenUsedCheckBox;
    private CheckBox? _autoRedefineCheckBox;
    private CheckBox? _lockedCheckBox;
    private Button? _setDefaultButton;
    private Button? _modifyButton;
    private Button? _updateButton;
    private IReadOnlyList<EditorStyleInfo> _allStyles = Array.Empty<EditorStyleInfo>();

    public ManageStylesDialog(IStyleManagerService styleService, IFontService? fontService)
    {
        InitializeComponent();
        InitializeControls();
        SetServices(styleService, fontService);
    }

    public void SetServices(IStyleManagerService styleService, IFontService? fontService)
    {
        _styleService = styleService ?? throw new ArgumentNullException(nameof(styleService));
        _fontService = fontService;
        RefreshStyles();
    }

    private void InitializeControls()
    {
        _stylesList = this.FindControl<ListBox>("StylesList");
        _searchBox = this.FindControl<TextBox>("StyleSearchBox");
        _typeCombo = this.FindControl<ComboBox>("StyleTypeCombo");
        _styleNameBox = this.FindControl<TextBox>("StyleNameBox");
        _styleTypeText = this.FindControl<TextBlock>("StyleTypeText");
        _basedOnCombo = this.FindControl<ComboBox>("StyleBasedOnCombo");
        _nextStyleCombo = this.FindControl<ComboBox>("StyleNextCombo");
        _priorityBox = this.FindControl<TextBox>("PriorityBox");
        _quickStyleCheckBox = this.FindControl<CheckBox>("QuickStyleCheckBox");
        _hiddenCheckBox = this.FindControl<CheckBox>("HiddenCheckBox");
        _semiHiddenCheckBox = this.FindControl<CheckBox>("SemiHiddenCheckBox");
        _unhideWhenUsedCheckBox = this.FindControl<CheckBox>("UnhideWhenUsedCheckBox");
        _autoRedefineCheckBox = this.FindControl<CheckBox>("AutoRedefineCheckBox");
        _lockedCheckBox = this.FindControl<CheckBox>("LockedCheckBox");
        _setDefaultButton = this.FindControl<Button>("SetDefaultButton");
        _modifyButton = this.FindControl<Button>("ModifyButton");
        _updateButton = this.FindControl<Button>("UpdateButton");

        if (_stylesList is not null)
        {
            _stylesList.SelectionChanged += OnStyleSelectionChanged;
            _stylesList.KeyDown += OnStylesKeyDown;
        }

        if (_searchBox is not null)
        {
            _searchBox.TextChanged += (_, _) => RefreshStyles();
        }

        if (_typeCombo is not null)
        {
            _typeCombo.SelectionChanged += (_, _) => RefreshStyles();
            if (_typeCombo.SelectedIndex < 0)
            {
                _typeCombo.SelectedIndex = 0;
            }
        }

        if (_setDefaultButton is not null)
        {
            _setDefaultButton.Click += OnSetDefaultClick;
        }

        if (_modifyButton is not null)
        {
            _modifyButton.Click += OnModifyClick;
        }

        if (_updateButton is not null)
        {
            _updateButton.Click += OnUpdateClick;
        }

        if (this.FindControl<Button>("NewStyleButton") is { } newStyleButton)
        {
            newStyleButton.Click += OnNewStyleClick;
        }

        if (this.FindControl<Button>("CloseButton") is { } closeButton)
        {
            closeButton.Click += (_, _) => Close();
        }
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

        _allStyles = _styleService.GetStyles();
        var search = _searchBox?.Text;
        var typeFilter = _typeCombo?.SelectedIndex ?? 0;
        var filtered = new List<EditorStyleInfo>(_allStyles.Count);
        foreach (var style in _allStyles)
        {
            if (!MatchesTypeFilter(style, typeFilter))
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

        filtered.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
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

    private void OnStyleSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        UpdateStyleDetails();
    }

    private void OnStylesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        UpdateStyleDetails();
        e.Handled = true;
    }

    private void UpdateStyleDetails()
    {
        if (_styleService is null || _stylesList?.SelectedItem is not EditorStyleInfo style)
        {
            SetManageEnabled(false);
            return;
        }

        var definition = GetStyleDefinition(style);
        var isLocked = definition?.Locked == true;
        SetManageEnabled(!isLocked);

        if (_styleNameBox is not null)
        {
            _styleNameBox.Text = definition?.Name ?? style.Name;
        }

        if (_styleTypeText is not null)
        {
            _styleTypeText.Text = style.Type.ToString();
        }

        if (_basedOnCombo is not null)
        {
            _basedOnCombo.ItemsSource = BuildStyleComboItems(style.Type, style.Id);
            SelectStyleComboItem(_basedOnCombo, definition?.BasedOnId);
        }

        if (_nextStyleCombo is not null)
        {
            _nextStyleCombo.ItemsSource = BuildStyleComboItems(style.Type, style.Id);
            SelectStyleComboItem(_nextStyleCombo, definition?.NextStyleId);
        }

        if (_priorityBox is not null)
        {
            _priorityBox.Text = definition?.UiPriority?.ToString(CultureInfo.InvariantCulture);
        }

        if (_quickStyleCheckBox is not null)
        {
            _quickStyleCheckBox.IsChecked = definition?.QuickStyle;
        }

        if (_hiddenCheckBox is not null)
        {
            _hiddenCheckBox.IsChecked = definition?.Hidden;
        }

        if (_semiHiddenCheckBox is not null)
        {
            _semiHiddenCheckBox.IsChecked = definition?.SemiHidden;
        }

        if (_unhideWhenUsedCheckBox is not null)
        {
            _unhideWhenUsedCheckBox.IsChecked = definition?.UnhideWhenUsed;
        }

        if (_autoRedefineCheckBox is not null)
        {
            _autoRedefineCheckBox.IsChecked = definition?.AutoRedefine;
        }

        if (_lockedCheckBox is not null)
        {
            _lockedCheckBox.IsChecked = definition?.Locked;
        }

        if (_setDefaultButton is not null)
        {
            _setDefaultButton.IsEnabled = !isLocked && !style.IsDefault;
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

        if (_priorityBox is not null)
        {
            _priorityBox.IsEnabled = enabled;
        }

        if (_quickStyleCheckBox is not null)
        {
            _quickStyleCheckBox.IsEnabled = enabled;
        }

        if (_hiddenCheckBox is not null)
        {
            _hiddenCheckBox.IsEnabled = enabled;
        }

        if (_semiHiddenCheckBox is not null)
        {
            _semiHiddenCheckBox.IsEnabled = enabled;
        }

        if (_unhideWhenUsedCheckBox is not null)
        {
            _unhideWhenUsedCheckBox.IsEnabled = enabled;
        }

        if (_autoRedefineCheckBox is not null)
        {
            _autoRedefineCheckBox.IsEnabled = enabled;
        }

        if (_lockedCheckBox is not null)
        {
            _lockedCheckBox.IsEnabled = enabled;
        }

        if (_modifyButton is not null)
        {
            _modifyButton.IsEnabled = enabled;
        }

        if (_updateButton is not null)
        {
            _updateButton.IsEnabled = enabled;
        }
    }

    private void OnSetDefaultClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService is null || _stylesList?.SelectedItem is not EditorStyleInfo style)
        {
            return;
        }

        if (_styleService.SetDefaultStyle(style.Type, style.Id))
        {
            RefreshStyles();
        }
    }

    private void OnUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService is null || _stylesList?.SelectedItem is not EditorStyleInfo style)
        {
            return;
        }

        var name = _styleNameBox?.Text ?? string.Empty;
        var basedOnId = (_basedOnCombo?.SelectedItem as StyleComboItem)?.Id;
        var nextStyleId = (_nextStyleCombo?.SelectedItem as StyleComboItem)?.Id;
        var quickStyle = _quickStyleCheckBox?.IsChecked;
        var hidden = _hiddenCheckBox?.IsChecked;
        var semiHidden = _semiHiddenCheckBox?.IsChecked;
        var unhideWhenUsed = _unhideWhenUsedCheckBox?.IsChecked;
        var autoRedefine = _autoRedefineCheckBox?.IsChecked;
        var locked = _lockedCheckBox?.IsChecked;
        var priority = ParsePriority(_priorityBox?.Text);

        var changed = false;
        if (!string.IsNullOrWhiteSpace(name))
        {
            changed |= _styleService.RenameStyle(style.Type, style.Id, name);
        }

        changed |= _styleService.SetStyleBasedOn(style.Type, style.Id, basedOnId);
        changed |= _styleService.SetStyleNext(style.Type, style.Id, nextStyleId);
        changed |= _styleService.SetStyleQuickStyle(style.Type, style.Id, quickStyle);
        changed |= _styleService.SetStyleHidden(style.Type, style.Id, hidden);
        changed |= _styleService.SetStyleSemiHidden(style.Type, style.Id, semiHidden);
        changed |= _styleService.SetStyleUnhideWhenUsed(style.Type, style.Id, unhideWhenUsed);
        changed |= _styleService.SetStyleAutoRedefine(style.Type, style.Id, autoRedefine);
        changed |= _styleService.SetStylePriority(style.Type, style.Id, priority);
        changed |= _styleService.SetStyleLocked(style.Type, style.Id, locked);

        if (changed)
        {
            RefreshStyles();
        }
    }

    private async void OnModifyClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService is null || _stylesList?.SelectedItem is not EditorStyleInfo style)
        {
            return;
        }

        var state = BuildEditorState(style);
        var dialog = new StyleEditorDialog(state, _styleService, _fontService);
        var result = await dialog.ShowDialog<StyleEditorResult?>(this);
        if (result is not StyleEditorResult updated)
        {
            return;
        }

        ApplyStyleUpdate(style.Type, style.Id, updated);
        RefreshStyles();
    }

    private async void OnNewStyleClick(object? sender, RoutedEventArgs e)
    {
        if (_styleService is null)
        {
            return;
        }

        var state = new StyleEditorState(EditorStyleType.Paragraph, string.Empty, null, null, false, false, null, null, null, null, null);
        var dialog = new StyleEditorDialog(state, _styleService, _fontService);
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

    private void ApplyStyleUpdate(EditorStyleType type, string styleId, StyleEditorResult result)
    {
        _styleService?.RenameStyle(type, styleId, result.Name);
        _styleService?.SetStyleBasedOn(type, styleId, result.BasedOnId);
        _styleService?.SetStyleNext(type, styleId, result.NextStyleId);
        _styleService?.SetStyleQuickStyle(type, styleId, result.QuickStyle);
        _styleService?.SetStyleAutoRedefine(type, styleId, result.AutoRedefine);

        switch (type)
        {
            case EditorStyleType.Paragraph:
                _styleService?.UpdateParagraphStyleProperties(styleId, result.RunProperties, result.ParagraphProperties);
                break;
            case EditorStyleType.Character:
                _styleService?.UpdateCharacterStyleProperties(styleId, result.RunProperties);
                break;
            case EditorStyleType.Table:
                _styleService?.UpdateTableStyleProperties(styleId, result.TableProperties, result.TableCellProperties);
                break;
            default:
                break;
        }
    }

    private StyleDefinitionDetails? GetStyleDefinition(EditorStyleInfo style)
    {
        if (_styleService is null)
        {
            return null;
        }

        return style.Type switch
        {
            EditorStyleType.Paragraph => Wrap(_styleService.GetParagraphStyleDefinition(style.Id)),
            EditorStyleType.Character => Wrap(_styleService.GetCharacterStyleDefinition(style.Id)),
            EditorStyleType.Table => Wrap(_styleService.GetTableStyleDefinition(style.Id)),
            _ => null
        };
    }

    private static StyleDefinitionDetails? Wrap(ParagraphStyleDefinition? definition)
    {
        return definition is null
            ? null
            : new StyleDefinitionDetails(
                definition.Id,
                definition.Name,
                definition.BasedOnId,
                definition.NextStyleId,
                definition.QuickStyle,
                definition.Hidden,
                definition.SemiHidden,
                definition.UnhideWhenUsed,
                definition.AutoRedefine,
                definition.Locked,
                definition.UiPriority,
                definition.RunProperties.Clone(),
                CloneParagraphStyleProperties(definition.ParagraphProperties),
                null,
                null);
    }

    private static StyleDefinitionDetails? Wrap(CharacterStyleDefinition? definition)
    {
        return definition is null
            ? null
            : new StyleDefinitionDetails(
                definition.Id,
                definition.Name,
                definition.BasedOnId,
                definition.NextStyleId,
                definition.QuickStyle,
                definition.Hidden,
                definition.SemiHidden,
                definition.UnhideWhenUsed,
                definition.AutoRedefine,
                definition.Locked,
                definition.UiPriority,
                definition.RunProperties.Clone(),
                null,
                null,
                null);
    }

    private static StyleDefinitionDetails? Wrap(TableStyleDefinition? definition)
    {
        return definition is null
            ? null
            : new StyleDefinitionDetails(
                definition.Id,
                definition.Name,
                definition.BasedOnId,
                definition.NextStyleId,
                definition.QuickStyle,
                definition.Hidden,
                definition.SemiHidden,
                definition.UnhideWhenUsed,
                definition.AutoRedefine,
                definition.Locked,
                definition.UiPriority,
                null,
                null,
                definition.TableProperties.Clone(),
                definition.CellProperties.Clone());
    }

    private StyleEditorState BuildEditorState(EditorStyleInfo info)
    {
        var definition = GetStyleDefinition(info);
        return new StyleEditorState(
            info.Type,
            definition?.Name ?? info.Name,
            definition?.BasedOnId,
            definition?.NextStyleId,
            definition?.QuickStyle,
            definition?.AutoRedefine,
            definition?.RunProperties,
            definition?.ParagraphProperties,
            definition?.TableProperties,
            definition?.TableCellProperties,
            info.Id);
    }

    private List<StyleComboItem> BuildStyleComboItems(EditorStyleType type, string currentStyleId)
    {
        var items = new List<StyleComboItem> { new StyleComboItem(null, "None") };
        foreach (var style in _allStyles)
        {
            if (style.Type != type)
            {
                continue;
            }

            if (string.Equals(style.Id, currentStyleId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

    private static bool MatchesTypeFilter(EditorStyleInfo style, int typeFilter)
    {
        return typeFilter switch
        {
            1 => style.Type == EditorStyleType.Paragraph,
            2 => style.Type == EditorStyleType.Character,
            3 => style.Type == EditorStyleType.Table,
            _ => true
        };
    }

    private static int? ParsePriority(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? value
            : (int?)null;
    }

    private sealed record StyleComboItem(string? Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record StyleDefinitionDetails(
        string Id,
        string? Name,
        string? BasedOnId,
        string? NextStyleId,
        bool? QuickStyle,
        bool? Hidden,
        bool? SemiHidden,
        bool? UnhideWhenUsed,
        bool? AutoRedefine,
        bool? Locked,
        int? UiPriority,
        TextStyleProperties? RunProperties,
        ParagraphStyleProperties? ParagraphProperties,
        TableProperties? TableProperties,
        TableCellProperties? TableCellProperties);

    private static ParagraphStyleProperties CloneParagraphStyleProperties(ParagraphStyleProperties source)
    {
        var clone = new ParagraphStyleProperties
        {
            Alignment = source.Alignment,
            SpacingBefore = source.SpacingBefore,
            SpacingAfter = source.SpacingAfter,
            SpacingBeforeLines = source.SpacingBeforeLines,
            SpacingAfterLines = source.SpacingAfterLines,
            AutoSpacingBefore = source.AutoSpacingBefore,
            AutoSpacingAfter = source.AutoSpacingAfter,
            LineSpacing = source.LineSpacing,
            LineSpacingRule = source.LineSpacingRule,
            IndentLeft = source.IndentLeft,
            IndentRight = source.IndentRight,
            FirstLineIndent = source.FirstLineIndent,
            KeepWithNext = source.KeepWithNext,
            KeepLinesTogether = source.KeepLinesTogether,
            WidowControl = source.WidowControl,
            PageBreakBefore = source.PageBreakBefore,
            ContextualSpacing = source.ContextualSpacing,
            Bidi = source.Bidi,
            TextDirection = source.TextDirection,
            EastAsianLayout = source.EastAsianLayout?.Clone(),
            ShadingColor = source.ShadingColor,
            SuppressLineNumbers = source.SuppressLineNumbers,
            DropCap = source.DropCap?.Clone(),
            Frame = source.Frame?.Clone()
        };

        foreach (var tab in source.TabStops)
        {
            clone.TabStops.Add(tab.Clone());
        }

        clone.Borders.Top = source.Borders.Top?.Clone();
        clone.Borders.Bottom = source.Borders.Bottom?.Clone();
        clone.Borders.Left = source.Borders.Left?.Clone();
        clone.Borders.Right = source.Borders.Right?.Clone();
        return clone;
    }
}

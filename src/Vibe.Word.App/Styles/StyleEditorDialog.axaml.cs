using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Word.App;

public readonly record struct StyleEditorState(
    EditorStyleType Type,
    string Name,
    string? BasedOnId,
    string? NextStyleId,
    bool? QuickStyle,
    bool? AutoRedefine,
    TextStyleProperties? RunProperties,
    ParagraphStyleProperties? ParagraphProperties,
    TableProperties? TableProperties,
    TableCellProperties? TableCellProperties,
    string? StyleId);

public readonly record struct StyleEditorResult(
    EditorStyleType Type,
    string Name,
    string? BasedOnId,
    string? NextStyleId,
    bool QuickStyle,
    bool AutoRedefine,
    TextStyleProperties? RunProperties,
    ParagraphStyleProperties? ParagraphProperties,
    TableProperties? TableProperties,
    TableCellProperties? TableCellProperties,
    string? StyleId);

public partial class StyleEditorDialog : Window
{
    private const float PointsToDipScale = 96f / 72f;
    private const float TwipsPerLine = 240f;
    private const float TwipsPerPoint = 20f;

    private readonly IStyleManagerService _styleService;
    private readonly IFontService? _fontService;
    private readonly TextBox _styleNameBox;
    private readonly ComboBox _styleTypeCombo;
    private readonly ComboBox _basedOnCombo;
    private readonly ComboBox _nextStyleCombo;
    private readonly CheckBox _quickStyleCheckBox;
    private readonly CheckBox _autoRedefineCheckBox;
    private readonly Button _fontButton;
    private readonly Button _paragraphButton;
    private readonly Button _tableButton;
    private TextStyleProperties? _runProperties;
    private ParagraphStyleProperties? _paragraphProperties;
    private TableProperties? _tableProperties;
    private TableCellProperties? _tableCellProperties;
    private string? _styleId;

    public StyleEditorDialog(StyleEditorState state, IStyleManagerService styleService, IFontService? fontService)
    {
        _styleService = styleService ?? throw new ArgumentNullException(nameof(styleService));
        _fontService = fontService;
        InitializeComponent();
        _styleNameBox = this.FindControl<TextBox>("StyleNameBox")!;
        _styleTypeCombo = this.FindControl<ComboBox>("StyleTypeCombo")!;
        _basedOnCombo = this.FindControl<ComboBox>("BasedOnCombo")!;
        _nextStyleCombo = this.FindControl<ComboBox>("NextStyleCombo")!;
        _quickStyleCheckBox = this.FindControl<CheckBox>("QuickStyleCheckBox")!;
        _autoRedefineCheckBox = this.FindControl<CheckBox>("AutoRedefineCheckBox")!;
        _fontButton = this.FindControl<Button>("FontButton")!;
        _paragraphButton = this.FindControl<Button>("ParagraphButton")!;
        _tableButton = this.FindControl<Button>("TableButton")!;

        _styleTypeCombo.SelectionChanged += (_, _) => UpdateStyleTypeControls();
        _fontButton.Click += OnFontClick;
        _paragraphButton.Click += OnParagraphClick;
        _tableButton.Click += OnTableClick;

        if (this.FindControl<Button>("OkButton") is { } okButton)
        {
            okButton.Click += OnOkClick;
        }

        if (this.FindControl<Button>("CancelButton") is { } cancelButton)
        {
            cancelButton.Click += (_, _) => Close(null);
        }

        SetState(state);
    }

    private void SetState(StyleEditorState state)
    {
        _styleId = state.StyleId;
        _styleNameBox.Text = state.Name;
        _styleTypeCombo.SelectedIndex = (int)state.Type;
        _styleTypeCombo.IsEnabled = string.IsNullOrWhiteSpace(state.StyleId);
        _quickStyleCheckBox.IsChecked = state.QuickStyle;
        _autoRedefineCheckBox.IsChecked = state.AutoRedefine;
        _runProperties = state.RunProperties?.Clone();
        _paragraphProperties = state.ParagraphProperties is null ? null : CloneParagraphStyleProperties(state.ParagraphProperties);
        _tableProperties = state.TableProperties?.Clone();
        _tableCellProperties = state.TableCellProperties?.Clone();
        UpdateStyleTypeControls();
        SelectStyleComboItem(_basedOnCombo, state.BasedOnId);
        SelectStyleComboItem(_nextStyleCombo, state.NextStyleId);
    }

    private void UpdateStyleTypeControls()
    {
        var type = GetSelectedType();
        _basedOnCombo.ItemsSource = BuildStyleComboItems(type, _styleId);
        _nextStyleCombo.ItemsSource = BuildStyleComboItems(type, _styleId);

        _fontButton.IsEnabled = type != EditorStyleType.Table;
        _paragraphButton.IsEnabled = type == EditorStyleType.Paragraph;
        _tableButton.IsEnabled = type == EditorStyleType.Table;
    }

    private EditorStyleType GetSelectedType()
    {
        return _styleTypeCombo.SelectedIndex switch
        {
            1 => EditorStyleType.Character,
            2 => EditorStyleType.Table,
            _ => EditorStyleType.Paragraph
        };
    }

    private async void OnFontClick(object? sender, RoutedEventArgs e)
    {
        var fonts = ResolveFontFamilies();
        var dialog = new FontDialog(fonts, BuildFontDialogState());
        var result = await dialog.ShowDialog<EditorFontDialogOptions?>(this);
        if (result is EditorFontDialogOptions options)
        {
            ApplyFontOptions(options);
        }
    }

    private async void OnParagraphClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new ParagraphDialog(BuildParagraphDialogState(_paragraphProperties));
        var result = await dialog.ShowDialog<EditorParagraphDialogOptions?>(this);
        if (result is EditorParagraphDialogOptions options)
        {
            ApplyParagraphOptions(options);
        }
    }

    private async void OnTableClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new TablePropertiesDialog(BuildTableDialogState());
        var result = await dialog.ShowDialog<EditorTablePropertiesDialogOptions?>(this);
        if (result is EditorTablePropertiesDialogOptions options)
        {
            ApplyTableOptions(options);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var name = _styleNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var type = GetSelectedType();
        var basedOnId = (_basedOnCombo.SelectedItem as StyleComboItem)?.Id;
        var nextStyleId = (_nextStyleCombo.SelectedItem as StyleComboItem)?.Id;
        var result = new StyleEditorResult(
            type,
            name,
            basedOnId,
            nextStyleId,
            _quickStyleCheckBox.IsChecked == true,
            _autoRedefineCheckBox.IsChecked == true,
            type == EditorStyleType.Table ? null : _runProperties?.Clone(),
            type == EditorStyleType.Paragraph && _paragraphProperties is not null ? CloneParagraphStyleProperties(_paragraphProperties) : null,
            type == EditorStyleType.Table ? _tableProperties?.Clone() : null,
            type == EditorStyleType.Table ? _tableCellProperties?.Clone() : null,
            _styleId);

        Close(result);
    }

    private IReadOnlyList<string> ResolveFontFamilies()
    {
        if (_fontService is null)
        {
            return Array.Empty<string>();
        }

        var families = _fontService.GetFontFamilies();
        var list = new List<string>(families.Count);
        foreach (var family in families)
        {
            list.Add(family.Name);
        }

        return list;
    }

    private FontDialogState BuildFontDialogState()
    {
        var properties = _runProperties;
        var effects = properties?.Effects;
        return new FontDialogState(
            properties?.FontFamily,
            properties?.FontSize,
            properties?.FontWeight,
            properties?.FontStyle,
            properties?.UnderlineStyle,
            properties?.UnderlineColor,
            properties?.Color,
            properties?.Strikethrough,
            properties?.SmallCaps,
            properties?.Caps,
            properties?.VerticalPosition,
            effects?.Outline?.Enabled,
            effects?.Shadow?.Enabled,
            effects?.Emboss,
            effects?.Imprint,
            properties?.HorizontalScale is null ? null : properties.HorizontalScale.Value * 100f,
            properties?.LetterSpacing is null ? null : DipToPoints(properties.LetterSpacing.Value),
            properties?.BaselineOffset is null ? null : DipToPoints(properties.BaselineOffset.Value));
    }

    private static ParagraphDialogState BuildParagraphDialogState(ParagraphStyleProperties? properties)
    {
        if (properties is null)
        {
            return new ParagraphDialogState(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var specialIndent = ResolveSpecialIndent(properties.FirstLineIndent, out var specialByPoints);
        ResolveLineSpacing(properties.LineSpacing, properties.LineSpacingRule, out var lineSpacingKind, out var lineSpacingAt);

        return new ParagraphDialogState(
            properties.Alignment,
            properties.TextDirection,
            properties.IndentLeft is null ? null : DipToPoints(properties.IndentLeft.Value),
            properties.IndentRight is null ? null : DipToPoints(properties.IndentRight.Value),
            specialIndent,
            specialByPoints,
            properties.SpacingBefore is null ? null : DipToPoints(properties.SpacingBefore.Value),
            properties.SpacingAfter is null ? null : DipToPoints(properties.SpacingAfter.Value),
            lineSpacingKind,
            lineSpacingAt,
            properties.ContextualSpacing,
            properties.Bidi,
            properties.WidowControl,
            properties.KeepWithNext,
            properties.KeepLinesTogether,
            properties.PageBreakBefore,
            properties.SuppressLineNumbers);
    }

    private TablePropertiesDialogState BuildTableDialogState()
    {
        var table = _tableProperties;
        var cell = _tableCellProperties;
        return new TablePropertiesDialogState(
            table?.Alignment,
            table?.Width is null ? null : DipToPoints(table.Width.Value),
            table?.WidthUnit,
            table?.Indent is null ? null : DipToPoints(table.Indent.Value),
            table?.IndentUnit,
            table?.LayoutMode,
            table?.CellSpacing is null ? null : DipToPoints(table.CellSpacing.Value),
            table?.CellSpacingUnit,
            table?.CellPadding is null ? null : new DocThickness(
                DipToPoints(table.CellPadding.Value.Left),
                DipToPoints(table.CellPadding.Value.Top),
                DipToPoints(table.CellPadding.Value.Right),
                DipToPoints(table.CellPadding.Value.Bottom)),
            null,
            null,
            null,
            null,
            null,
            cell?.VerticalAlignment);
    }

    private void ApplyFontOptions(EditorFontDialogOptions options)
    {
        var style = _runProperties?.Clone() ?? new TextStyleProperties();
        if (!string.IsNullOrWhiteSpace(options.FontFamily))
        {
            style.FontFamily = options.FontFamily;
        }

        if (options.FontSize.HasValue)
        {
            style.FontSize = options.FontSize.Value;
        }

        if (options.FontWeight.HasValue)
        {
            style.FontWeight = options.FontWeight.Value;
        }

        if (options.FontStyle.HasValue)
        {
            style.FontStyle = options.FontStyle.Value;
        }

        if (options.UnderlineStyle.HasValue)
        {
            style.UnderlineStyle = options.UnderlineStyle.Value;
            style.Underline = options.UnderlineStyle.Value != DocUnderlineStyle.None;
        }

        if (options.UnderlineColor.HasValue)
        {
            style.UnderlineColor = options.UnderlineColor;
        }

        if (options.FontColor.HasValue)
        {
            style.Color = options.FontColor.Value;
        }

        if (options.Strikethrough.HasValue)
        {
            style.Strikethrough = options.Strikethrough.Value;
        }

        if (options.SmallCaps.HasValue)
        {
            style.SmallCaps = options.SmallCaps.Value;
        }

        if (options.Caps.HasValue)
        {
            style.Caps = options.Caps.Value;
        }

        if (options.VerticalPosition.HasValue)
        {
            style.VerticalPosition = options.VerticalPosition.Value;
        }

        if (options.LetterSpacing.HasValue)
        {
            style.LetterSpacing = options.LetterSpacing.Value;
        }

        if (options.HorizontalScale.HasValue)
        {
            style.HorizontalScale = MathF.Max(0.1f, options.HorizontalScale.Value);
        }

        if (options.BaselineOffset.HasValue)
        {
            style.BaselineOffset = options.BaselineOffset.Value;
        }

        ApplyTextEffectsFromOptions(style, options);
        _runProperties = style;
    }

    private void ApplyParagraphOptions(EditorParagraphDialogOptions options)
    {
        var properties = _paragraphProperties is null ? new ParagraphStyleProperties() : CloneParagraphStyleProperties(_paragraphProperties);
        if (options.Alignment.HasValue)
        {
            properties.Alignment = options.Alignment.Value;
        }

        if (options.IndentLeft.HasValue)
        {
            properties.IndentLeft = options.IndentLeft.Value;
        }

        if (options.IndentRight.HasValue)
        {
            properties.IndentRight = options.IndentRight.Value;
        }

        if (options.FirstLineIndent.HasValue)
        {
            properties.FirstLineIndent = options.FirstLineIndent.Value;
        }

        if (options.SpacingBefore.HasValue)
        {
            properties.SpacingBefore = options.SpacingBefore.Value;
        }

        if (options.SpacingAfter.HasValue)
        {
            properties.SpacingAfter = options.SpacingAfter.Value;
        }

        if (options.LineSpacing.HasValue)
        {
            properties.LineSpacing = options.LineSpacing.Value;
        }

        if (options.LineSpacingRule.HasValue)
        {
            properties.LineSpacingRule = options.LineSpacingRule.Value;
        }

        if (options.ContextualSpacing.HasValue)
        {
            properties.ContextualSpacing = options.ContextualSpacing.Value;
        }

        if (options.KeepWithNext.HasValue)
        {
            properties.KeepWithNext = options.KeepWithNext.Value;
        }

        if (options.KeepLinesTogether.HasValue)
        {
            properties.KeepLinesTogether = options.KeepLinesTogether.Value;
        }

        if (options.WidowControl.HasValue)
        {
            properties.WidowControl = options.WidowControl.Value;
        }

        if (options.PageBreakBefore.HasValue)
        {
            properties.PageBreakBefore = options.PageBreakBefore.Value;
        }

        if (options.SuppressLineNumbers.HasValue)
        {
            properties.SuppressLineNumbers = options.SuppressLineNumbers.Value;
        }

        if (options.Bidi.HasValue)
        {
            properties.Bidi = options.Bidi.Value;
        }

        if (options.TextDirection.HasValue)
        {
            properties.TextDirection = options.TextDirection.Value;
        }

        _paragraphProperties = properties;
    }

    private void ApplyTableOptions(EditorTablePropertiesDialogOptions options)
    {
        var table = _tableProperties?.Clone() ?? new TableProperties();
        table.Alignment = options.Alignment;
        table.Width = options.PreferredWidth;
        table.WidthUnit = options.PreferredWidthUnit;
        table.Indent = options.Indent;
        table.IndentUnit = options.IndentUnit;
        table.LayoutMode = options.LayoutMode;
        table.CellSpacing = options.CellSpacing;
        table.CellSpacingUnit = options.CellSpacingUnit;
        if (options.CellPadding.HasValue)
        {
            table.CellPadding = options.CellPadding.Value;
        }

        _tableProperties = table;
        var cell = _tableCellProperties?.Clone() ?? new TableCellProperties();
        if (options.CellPadding.HasValue)
        {
            cell.Padding = options.CellPadding.Value;
        }

        cell.VerticalAlignment = options.CellVerticalAlignment;
        _tableCellProperties = cell;
    }

    private static void ApplyTextEffectsFromOptions(TextStyleProperties style, EditorFontDialogOptions options)
    {
        if (!options.TextOutline.HasValue
            && !options.TextShadow.HasValue
            && !options.TextEmboss.HasValue
            && !options.TextImprint.HasValue)
        {
            return;
        }

        var effects = style.Effects?.Clone() ?? new TextEffects();
        if (options.TextOutline.HasValue)
        {
            effects.Outline = options.TextOutline.Value ? new TextOutlineEffect { Enabled = true } : null;
        }

        if (options.TextShadow.HasValue)
        {
            effects.Shadow = options.TextShadow.Value ? new TextShadowEffect { Enabled = true } : null;
        }

        if (options.TextEmboss.HasValue)
        {
            effects.Emboss = options.TextEmboss.Value;
        }

        if (options.TextImprint.HasValue)
        {
            effects.Imprint = options.TextImprint.Value;
        }

        style.Effects = effects.HasValues ? effects : null;
    }

    private List<StyleComboItem> BuildStyleComboItems(EditorStyleType type, string? currentStyleId)
    {
        var items = new List<StyleComboItem> { new StyleComboItem(null, "None") };
        foreach (var style in _styleService.GetStyles(type))
        {
            if (!string.IsNullOrWhiteSpace(currentStyleId)
                && string.Equals(style.Id, currentStyleId, StringComparison.OrdinalIgnoreCase))
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

    private static ParagraphSpecialIndentKind? ResolveSpecialIndent(float? firstLineIndent, out float? byPoints)
    {
        byPoints = null;
        if (!firstLineIndent.HasValue)
        {
            return null;
        }

        if (firstLineIndent.Value > 0f)
        {
            byPoints = DipToPoints(firstLineIndent.Value);
            return ParagraphSpecialIndentKind.FirstLine;
        }

        if (firstLineIndent.Value < 0f)
        {
            byPoints = DipToPoints(MathF.Abs(firstLineIndent.Value));
            return ParagraphSpecialIndentKind.Hanging;
        }

        return ParagraphSpecialIndentKind.None;
    }

    private static void ResolveLineSpacing(int? lineSpacing, DocLineSpacingRule? rule, out LineSpacingOptionKind? kind, out float? atValue)
    {
        kind = null;
        atValue = null;
        if (!lineSpacing.HasValue)
        {
            return;
        }

        if (rule == DocLineSpacingRule.AtLeast)
        {
            kind = LineSpacingOptionKind.AtLeast;
            atValue = lineSpacing.Value / TwipsPerPoint;
            return;
        }

        if (rule == DocLineSpacingRule.Exactly)
        {
            kind = LineSpacingOptionKind.Exactly;
            atValue = lineSpacing.Value / TwipsPerPoint;
            return;
        }

        var multiple = lineSpacing.Value / (float)TwipsPerLine;
        if (NearlyEquals(multiple, 1f))
        {
            kind = LineSpacingOptionKind.Single;
            return;
        }

        if (NearlyEquals(multiple, 1.15f))
        {
            kind = LineSpacingOptionKind.One15;
            return;
        }

        if (NearlyEquals(multiple, 1.5f))
        {
            kind = LineSpacingOptionKind.One5;
            return;
        }

        if (NearlyEquals(multiple, 2f))
        {
            kind = LineSpacingOptionKind.Double;
            return;
        }

        kind = LineSpacingOptionKind.Multiple;
        atValue = multiple;
    }

    private static bool NearlyEquals(float left, float right)
    {
        return MathF.Abs(left - right) < 0.02f;
    }

    private static float DipToPoints(float dip)
    {
        return dip / PointsToDipScale;
    }

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

    private sealed record StyleComboItem(string? Id, string Name)
    {
        public override string ToString() => Name;
    }
}

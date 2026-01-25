using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Primitives;

namespace Vibe.Word.App;

public readonly record struct TablePropertiesDialogState(
    TableAlignment? Alignment,
    float? PreferredWidth,
    TableWidthUnit? PreferredWidthUnit,
    float? Indent,
    TableWidthUnit? IndentUnit,
    TableLayoutMode? LayoutMode,
    float? CellSpacing,
    TableWidthUnit? CellSpacingUnit,
    DocThickness? CellPaddingPoints,
    float? RowHeightPoints,
    TableRowHeightRule? RowHeightRule,
    bool? AllowRowBreak,
    bool? RepeatHeaderRows,
    float? ColumnWidthPoints,
    TableCellVerticalAlignment? CellVerticalAlignment);

public partial class TablePropertiesDialog : Window
{
    private const float PointsToDipScale = 96f / 72f;

    private readonly ComboBox _tableAlignmentCombo;
    private readonly TextBox _tableWidthTextBox;
    private readonly ComboBox _tableWidthUnitCombo;
    private readonly TextBox _tableIndentTextBox;
    private readonly ComboBox _tableIndentUnitCombo;
    private readonly ComboBox _tableLayoutCombo;
    private readonly TextBox _cellSpacingTextBox;
    private readonly ComboBox _cellSpacingUnitCombo;
    private readonly TextBox _rowHeightTextBox;
    private readonly ComboBox _rowHeightRuleCombo;
    private readonly CheckBox _allowRowBreakCheckBox;
    private readonly CheckBox _repeatHeaderRowsCheckBox;
    private readonly TextBox _columnWidthTextBox;
    private readonly ComboBox _cellVerticalAlignmentCombo;
    private readonly TextBox _cellMarginLeftTextBox;
    private readonly TextBox _cellMarginRightTextBox;
    private readonly TextBox _cellMarginTopTextBox;
    private readonly TextBox _cellMarginBottomTextBox;
    private TablePropertiesDialogState _state;

    public TablePropertiesDialog()
        : this(new TablePropertiesDialogState(
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
            null))
    {
    }

    public TablePropertiesDialog(TablePropertiesDialogState state)
    {
        InitializeComponent();
        _tableAlignmentCombo = this.FindControl<ComboBox>("TableAlignmentCombo")!;
        _tableWidthTextBox = this.FindControl<TextBox>("TableWidthTextBox")!;
        _tableWidthUnitCombo = this.FindControl<ComboBox>("TableWidthUnitCombo")!;
        _tableIndentTextBox = this.FindControl<TextBox>("TableIndentTextBox")!;
        _tableIndentUnitCombo = this.FindControl<ComboBox>("TableIndentUnitCombo")!;
        _tableLayoutCombo = this.FindControl<ComboBox>("TableLayoutCombo")!;
        _cellSpacingTextBox = this.FindControl<TextBox>("CellSpacingTextBox")!;
        _cellSpacingUnitCombo = this.FindControl<ComboBox>("CellSpacingUnitCombo")!;
        _rowHeightTextBox = this.FindControl<TextBox>("RowHeightTextBox")!;
        _rowHeightRuleCombo = this.FindControl<ComboBox>("RowHeightRuleCombo")!;
        _allowRowBreakCheckBox = this.FindControl<CheckBox>("AllowRowBreakCheckBox")!;
        _repeatHeaderRowsCheckBox = this.FindControl<CheckBox>("RepeatHeaderRowsCheckBox")!;
        _columnWidthTextBox = this.FindControl<TextBox>("ColumnWidthTextBox")!;
        _cellVerticalAlignmentCombo = this.FindControl<ComboBox>("CellVerticalAlignmentCombo")!;
        _cellMarginLeftTextBox = this.FindControl<TextBox>("CellMarginLeftTextBox")!;
        _cellMarginRightTextBox = this.FindControl<TextBox>("CellMarginRightTextBox")!;
        _cellMarginTopTextBox = this.FindControl<TextBox>("CellMarginTopTextBox")!;
        _cellMarginBottomTextBox = this.FindControl<TextBox>("CellMarginBottomTextBox")!;

        if (this.FindControl<Button>("OkButton") is { } okButton)
        {
            okButton.Click += OnOkClick;
        }

        if (this.FindControl<Button>("CancelButton") is { } cancelButton)
        {
            cancelButton.Click += OnCancelClick;
        }

        SetState(state);
    }

    public void SetState(TablePropertiesDialogState state)
    {
        _state = state;
        SelectComboByTag(_tableAlignmentCombo, state.Alignment);
        _tableWidthTextBox.Text = FormatValue(state.PreferredWidth);
        SelectComboByTag(_tableWidthUnitCombo, state.PreferredWidthUnit);
        _tableIndentTextBox.Text = FormatValue(state.Indent);
        SelectComboByTag(_tableIndentUnitCombo, state.IndentUnit);
        SelectComboByTag(_tableLayoutCombo, state.LayoutMode);
        _cellSpacingTextBox.Text = FormatValue(state.CellSpacing);
        SelectComboByTag(_cellSpacingUnitCombo, state.CellSpacingUnit);
        _rowHeightTextBox.Text = FormatValue(state.RowHeightPoints);
        SelectComboByTag(_rowHeightRuleCombo, state.RowHeightRule);
        _allowRowBreakCheckBox.IsChecked = state.AllowRowBreak;
        _repeatHeaderRowsCheckBox.IsChecked = state.RepeatHeaderRows;
        _columnWidthTextBox.Text = FormatValue(state.ColumnWidthPoints);
        SelectComboByTag(_cellVerticalAlignmentCombo, state.CellVerticalAlignment);

        if (state.CellPaddingPoints.HasValue)
        {
            var padding = state.CellPaddingPoints.Value;
            _cellMarginLeftTextBox.Text = FormatValue(padding.Left);
            _cellMarginRightTextBox.Text = FormatValue(padding.Right);
            _cellMarginTopTextBox.Text = FormatValue(padding.Top);
            _cellMarginBottomTextBox.Text = FormatValue(padding.Bottom);
        }
        else
        {
            _cellMarginLeftTextBox.Text = null;
            _cellMarginRightTextBox.Text = null;
            _cellMarginTopTextBox.Text = null;
            _cellMarginBottomTextBox.Text = null;
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var options = BuildOptions();
        if (options.HasValue)
        {
            Close(options.Value);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private EditorTablePropertiesDialogOptions? BuildOptions()
    {
        if (!TryParseNullable(_tableWidthTextBox.Text, out var preferredWidth)
            || !TryParseNullable(_tableIndentTextBox.Text, out var indent)
            || !TryParseNullable(_cellSpacingTextBox.Text, out var spacing)
            || !TryParseNullable(_rowHeightTextBox.Text, out var rowHeight)
            || !TryParseNullable(_columnWidthTextBox.Text, out var columnWidth)
            || !TryParseNullable(_cellMarginLeftTextBox.Text, out var marginLeft)
            || !TryParseNullable(_cellMarginRightTextBox.Text, out var marginRight)
            || !TryParseNullable(_cellMarginTopTextBox.Text, out var marginTop)
            || !TryParseNullable(_cellMarginBottomTextBox.Text, out var marginBottom))
        {
            return null;
        }

        var widthUnit = GetSelectedTag<TableWidthUnit>(_tableWidthUnitCombo);
        var indentUnit = GetSelectedTag<TableWidthUnit>(_tableIndentUnitCombo);
        var spacingUnit = GetSelectedTag<TableWidthUnit>(_cellSpacingUnitCombo);

        var preferredWidthValue = ResolveWidthValue(preferredWidth, widthUnit);
        var indentValue = ResolveWidthValue(indent, indentUnit);
        var spacingValue = ResolveWidthValue(spacing, spacingUnit);
        var rowHeightValue = rowHeight.HasValue ? PointsToDip(rowHeight.Value) : (float?)null;
        var columnWidthValue = columnWidth.HasValue ? PointsToDip(columnWidth.Value) : (float?)null;

        DocThickness? cellPadding = null;
        if (marginLeft.HasValue || marginRight.HasValue || marginTop.HasValue || marginBottom.HasValue)
        {
            var fallback = _state.CellPaddingPoints;
            var left = marginLeft ?? fallback?.Left ?? 0f;
            var right = marginRight ?? fallback?.Right ?? 0f;
            var top = marginTop ?? fallback?.Top ?? 0f;
            var bottom = marginBottom ?? fallback?.Bottom ?? 0f;
            cellPadding = new DocThickness(
                PointsToDip(left),
                PointsToDip(top),
                PointsToDip(right),
                PointsToDip(bottom));
        }

        var allowBreak = _allowRowBreakCheckBox.IsChecked;
        var cantSplit = allowBreak.HasValue ? !allowBreak.Value : (bool?)null;

        return new EditorTablePropertiesDialogOptions(
            Alignment: GetSelectedTag<TableAlignment>(_tableAlignmentCombo),
            PreferredWidth: preferredWidthValue,
            PreferredWidthUnit: widthUnit,
            Indent: indentValue,
            IndentUnit: indentUnit,
            LayoutMode: GetSelectedTag<TableLayoutMode>(_tableLayoutCombo),
            CellSpacing: spacingValue,
            CellSpacingUnit: spacingUnit,
            CellPadding: cellPadding,
            RowHeight: rowHeightValue,
            RowHeightRule: GetSelectedTag<TableRowHeightRule>(_rowHeightRuleCombo),
            CantSplit: cantSplit,
            RepeatHeaderRows: _repeatHeaderRowsCheckBox.IsChecked,
            ColumnWidth: columnWidthValue,
            CellVerticalAlignment: GetSelectedTag<TableCellVerticalAlignment>(_cellVerticalAlignmentCombo));
    }

    private static float? ResolveWidthValue(float? value, TableWidthUnit? unit)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (!unit.HasValue || unit == TableWidthUnit.Dxa)
        {
            return PointsToDip(MathF.Max(0f, value.Value));
        }

        if (unit == TableWidthUnit.Pct)
        {
            return MathF.Max(0f, value.Value);
        }

        return null;
    }

    private static float PointsToDip(float points) => points * PointsToDipScale;

    private static string? FormatValue(float? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.##", CultureInfo.CurrentCulture)
            : null;
    }

    private static bool TryParseNullable(string? text, out float? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static void SelectComboByTag<T>(ComboBox combo, T? value) where T : struct
    {
        if (!value.HasValue)
        {
            combo.SelectedIndex = -1;
            return;
        }

        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem { Tag: T tag } && EqualityComparer<T>.Default.Equals(tag, value.Value))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = -1;
    }

    private static T? GetSelectedTag<T>(ComboBox combo) where T : struct
    {
        return combo.SelectedItem is ComboBoxItem { Tag: T tag } ? tag : null;
    }
}

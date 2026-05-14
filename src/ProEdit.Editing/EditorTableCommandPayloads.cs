namespace ProEdit.Editing;

using ProEdit.Documents;
using ProEdit.Primitives;

public readonly record struct EditorTableColumnWidthsRequest(ReadOnlyMemory<float> ColumnWidths);
public readonly record struct EditorTableRowHeightRequest(int RowIndex, float Height, TableRowHeightRule Rule);

public readonly record struct EditorTablePropertiesDialogOptions(
    TableAlignment? Alignment,
    float? PreferredWidth,
    TableWidthUnit? PreferredWidthUnit,
    float? Indent,
    TableWidthUnit? IndentUnit,
    TableLayoutMode? LayoutMode,
    float? CellSpacing,
    TableWidthUnit? CellSpacingUnit,
    DocThickness? CellPadding,
    float? RowHeight,
    TableRowHeightRule? RowHeightRule,
    bool? CantSplit,
    bool? RepeatHeaderRows,
    float? ColumnWidth,
    TableCellVerticalAlignment? CellVerticalAlignment);

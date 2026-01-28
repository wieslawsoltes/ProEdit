using System.Buffers;
using System.Linq;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Layout;
using Vibe.Office.Primitives;

namespace Vibe.Word.Editor.Editing;

public sealed class EditorTableCommandMap
{
    private readonly EditorCommandRouterAdapter _router;
    private readonly IEditorMutableSession _session;

    public EditorTableCommandMap(EditorCommandRouterAdapter router, IEditorMutableSession session)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public void Register()
    {
        _router.RegisterAction(EditorTableCommandIds.Rows.InsertAbove, (_, __) => InsertRow(insertAbove: true), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Rows.InsertBelow, (_, __) => InsertRow(insertAbove: false), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Rows.Delete, (_, __) => DeleteRow(), CanEditTable);

        _router.RegisterAction(EditorTableCommandIds.Columns.InsertLeft, (_, __) => InsertColumn(insertLeft: true), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Columns.InsertRight, (_, __) => InsertColumn(insertLeft: false), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Columns.Delete, (_, __) => DeleteColumn(), CanEditTable);

        _router.RegisterAction(EditorTableCommandIds.Delete.Table, (_, __) => DeleteTable(), CanEditTable);

        _router.RegisterAction(EditorTableCommandIds.Merge.Cells, (_, __) => MergeCells(), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Merge.Split, (_, __) => SplitCells(), CanEditTable);

        _router.RegisterAction(EditorTableCommandIds.Layout.AutoFitContents, (_, __) => AutoFitContents(), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Layout.AutoFitWindow, (_, __) => AutoFitWindow(), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Layout.FixedColumnWidth, (_, __) => FixedColumnWidth(), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Layout.DistributeColumns, (_, __) => DistributeColumns(), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Layout.DistributeRows, (_, __) => DistributeRows(), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Layout.ColumnWidthsSet, (_, payload) => SetColumnWidths(payload), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Layout.RowHeightSet, (_, payload) => SetRowHeight(payload), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Layout.RepeatHeaderRows, (_, __) => ToggleRepeatHeaderRows(), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Layout.PropertiesApply, (_, payload) => ApplyTableProperties(payload), CanEditTable);

        _router.RegisterAction(EditorTableCommandIds.Alignment.AlignTop, (_, __) => ApplyVerticalAlignment(TableCellVerticalAlignment.Top), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Alignment.AlignMiddle, (_, __) => ApplyVerticalAlignment(TableCellVerticalAlignment.Center), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Alignment.AlignBottom, (_, __) => ApplyVerticalAlignment(TableCellVerticalAlignment.Bottom), CanEditTable);

        _router.RegisterAction(EditorTableCommandIds.Design.ApplyStyle, (_, payload) => ApplyTableStyle(payload), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Design.ToggleHeaderRow, (_, payload) => ToggleTableLook(payload, look => look.FirstRow, (look, value) => look.FirstRow = value), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Design.ToggleTotalRow, (_, payload) => ToggleTableLook(payload, look => look.LastRow, (look, value) => look.LastRow = value), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Design.ToggleFirstColumn, (_, payload) => ToggleTableLook(payload, look => look.FirstColumn, (look, value) => look.FirstColumn = value), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Design.ToggleLastColumn, (_, payload) => ToggleTableLook(payload, look => look.LastColumn, (look, value) => look.LastColumn = value), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Design.ToggleBandedRows, (_, payload) => ToggleTableLook(payload, look => look.BandedRows, (look, value) => look.BandedRows = value), CanEditTable);
        _router.RegisterAction(EditorTableCommandIds.Design.ToggleBandedColumns, (_, payload) => ToggleTableLook(payload, look => look.BandedColumns, (look, value) => look.BandedColumns = value), CanEditTable);
    }

    private bool CanEditTable(RibbonContextSnapshot? context, object? payload)
    {
        if (context.HasValue)
        {
            return context.Value.Selection.IsInTable;
        }

        return TryGetTableContext(out _);
    }

    private bool TryGetTableContext(out TableContext context)
    {
        context = default;
        if (_session.Document.ParagraphCount == 0)
        {
            return false;
        }

        var paragraphIndex = Math.Clamp(_session.Caret.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var location = _session.Document.GetParagraphLocation(paragraphIndex);
        if (!location.IsInTable || location.Table is null)
        {
            return false;
        }

        context = new TableContext(location.Table, location.BlockIndex, location.RowIndex, location.ColumnIndex);
        return true;
    }

    private bool TryGetSelectionRange(out TableSelectionRange range)
    {
        range = default;
        if (_session.Document.ParagraphCount == 0)
        {
            return false;
        }
        var selections = _session.TableSelections;
        if (selections.Count > 0)
        {
            range = ResolvePrimarySelection(selections).Normalize();
            return true;
        }

        var selection = _session.Selection.Normalize();
        var startIndex = Math.Clamp(selection.Start.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var endIndex = Math.Clamp(selection.End.ParagraphIndex, 0, _session.Document.ParagraphCount - 1);
        var startLocation = _session.Document.GetParagraphLocation(startIndex);
        var endLocation = _session.Document.GetParagraphLocation(endIndex);
        if (!startLocation.IsInTable || !endLocation.IsInTable || startLocation.Table is null || endLocation.Table is null)
        {
            return false;
        }

        if (!ReferenceEquals(startLocation.Table, endLocation.Table))
        {
            return false;
        }

        var rowStart = Math.Min(startLocation.RowIndex, endLocation.RowIndex);
        var rowEnd = Math.Max(startLocation.RowIndex, endLocation.RowIndex);
        var columnStart = Math.Min(startLocation.ColumnIndex, endLocation.ColumnIndex);
        var columnEnd = Math.Max(startLocation.ColumnIndex, endLocation.ColumnIndex);
        range = new TableSelectionRange(startLocation.Table, rowStart, rowEnd, columnStart, columnEnd);
        return true;
    }

    private TableSelectionRange ResolvePrimarySelection(IReadOnlyList<TableSelectionRange> selections)
    {
        if (TryGetTableContext(out var context))
        {
            for (var i = 0; i < selections.Count; i++)
            {
                if (ReferenceEquals(selections[i].Table, context.Table))
                {
                    return selections[i];
                }
            }
        }

        return selections[0];
    }

    private void InsertRow(bool insertAbove)
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        var table = range.Table;
        if (table.Rows.Count == 0)
        {
            table.Rows.Add(BuildEmptyRow(GetColumnCount(table)));
            _session.RefreshLayout();
            return;
        }

        var rowStart = Math.Clamp(range.RowStart, 0, table.Rows.Count - 1);
        var rowEnd = Math.Clamp(range.RowEnd, 0, table.Rows.Count - 1);
        if (rowEnd < rowStart)
        {
            (rowStart, rowEnd) = (rowEnd, rowStart);
        }

        var insertIndex = insertAbove ? rowStart : rowEnd + 1;
        insertIndex = Math.Clamp(insertIndex, 0, table.Rows.Count);
        var templateIndex = insertAbove ? rowStart : Math.Clamp(rowEnd, 0, table.Rows.Count - 1);
        var templateRow = table.Rows[templateIndex];
        var count = rowEnd - rowStart + 1;
        for (var i = 0; i < count; i++)
        {
            var newRow = BuildRowFromTemplate(table, templateRow, insertIndex + i);
            table.Rows.Insert(insertIndex + i, newRow);
        }

        _session.RefreshLayout();
    }

    private void InsertColumn(bool insertLeft)
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        var table = range.Table;
        if (table.Rows.Count == 0)
        {
            table.Rows.Add(BuildEmptyRow(GetColumnCount(table)));
            _session.RefreshLayout();
            return;
        }

        var columnCount = GetColumnCount(table);
        if (columnCount <= 0)
        {
            return;
        }

        var columnStart = Math.Clamp(range.ColumnStart, 0, columnCount - 1);
        var columnEnd = Math.Clamp(range.ColumnEnd, 0, columnCount - 1);
        if (columnEnd < columnStart)
        {
            (columnStart, columnEnd) = (columnEnd, columnStart);
        }

        var insertIndex = insertLeft ? columnStart : columnEnd + 1;
        var count = columnEnd - columnStart + 1;
        for (var i = 0; i < count; i++)
        {
            InsertColumnAt(table, insertIndex + i);
        }

        _session.RefreshLayout();
    }

    private void DeleteRow()
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        var table = range.Table;
        if (table.Rows.Count == 0)
        {
            return;
        }

        var rowStart = Math.Clamp(range.RowStart, 0, table.Rows.Count - 1);
        var rowEnd = Math.Clamp(range.RowEnd, 0, table.Rows.Count - 1);
        if (rowEnd < rowStart)
        {
            (rowStart, rowEnd) = (rowEnd, rowStart);
        }

        for (var rowIndex = rowEnd; rowIndex >= rowStart; rowIndex--)
        {
            table.Rows.RemoveAt(rowIndex);
        }

        if (table.Rows.Count == 0)
        {
            table.Rows.Add(BuildEmptyRow(GetColumnCount(table)));
        }

        NormalizeVerticalMerges(table);
        _session.RefreshLayout();
    }

    private void DeleteColumn()
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        var table = range.Table;
        var columnCount = GetColumnCount(table);
        if (columnCount <= 0)
        {
            return;
        }

        var columnStart = Math.Clamp(range.ColumnStart, 0, columnCount - 1);
        var columnEnd = Math.Clamp(range.ColumnEnd, 0, columnCount - 1);
        if (columnEnd < columnStart)
        {
            (columnStart, columnEnd) = (columnEnd, columnStart);
        }

        for (var columnIndex = columnEnd; columnIndex >= columnStart; columnIndex--)
        {
            RemoveColumnAt(table, columnIndex);
        }

        _session.RefreshLayout();
    }

    private void DeleteTable()
    {
        if (!TryGetTableContext(out var context))
        {
            return;
        }

        var document = _session.Document;
        if (context.BlockIndex < 0 || context.BlockIndex >= document.Blocks.Count)
        {
            return;
        }

        document.Blocks.RemoveAt(context.BlockIndex);
        document.Blocks.Insert(context.BlockIndex, new ParagraphBlock());
        var paragraphIndex = FindParagraphIndexForBlock(document, context.BlockIndex);
        _session.SetSelection(new TextRange(new TextPosition(paragraphIndex, 0), new TextPosition(paragraphIndex, 0)));
        _session.RefreshLayout();
    }

    private void ApplyTableStyle(object? payload)
    {
        if (!TryGetTableContext(out var context))
        {
            return;
        }

        var styleId = payload as string;
        context.Table.StyleId = string.IsNullOrWhiteSpace(styleId) ? null : styleId.Trim();
        _session.RefreshLayout();
    }

    private void ToggleTableLook(object? payload, Func<TableLook, bool> selector, Action<TableLook, bool> setter)
    {
        if (!TryGetTableContext(out var context))
        {
            return;
        }

        var properties = context.Table.Properties;
        var look = properties.Look ?? new TableLook();
        var current = selector(look);
        var next = payload is bool requested ? requested : !current;
        setter(look, next);
        properties.Look = look;
        _session.RefreshLayout();
    }

    private void ToggleRepeatHeaderRows()
    {
        if (!TryGetTableContext(out var context))
        {
            return;
        }

        var table = context.Table;
        if (table.Rows.Count == 0)
        {
            return;
        }

        var headerEnd = context.RowIndex;
        if (TryGetSelectionRange(out var range))
        {
            headerEnd = range.RowEnd;
        }

        headerEnd = Math.Clamp(headerEnd, 0, table.Rows.Count - 1);
        var shouldClear = true;
        for (var i = 0; i <= headerEnd; i++)
        {
            if (table.Rows[i].Properties.RepeatOnEachPage != true)
            {
                shouldClear = false;
                break;
            }
        }

        if (shouldClear)
        {
            for (var i = 0; i < table.Rows.Count; i++)
            {
                table.Rows[i].Properties.RepeatOnEachPage = false;
            }

            _session.RefreshLayout();
            return;
        }

        for (var i = 0; i < table.Rows.Count; i++)
        {
            table.Rows[i].Properties.RepeatOnEachPage = i <= headerEnd;
        }

        _session.RefreshLayout();
    }

    private void ApplyTableProperties(object? payload)
    {
        if (payload is not EditorTablePropertiesDialogOptions options)
        {
            return;
        }

        if (!TryGetTableContext(out var context))
        {
            return;
        }

        var table = context.Table;
        var updated = false;

        if (options.Alignment.HasValue)
        {
            table.Properties.Alignment = options.Alignment;
            updated = true;
        }

        if (options.LayoutMode.HasValue)
        {
            table.Properties.LayoutMode = options.LayoutMode;
            updated = true;
        }

        if (options.PreferredWidthUnit == TableWidthUnit.Auto)
        {
            table.Properties.Width = null;
            table.Properties.WidthUnit = TableWidthUnit.Auto;
            updated = true;
        }
        else if (options.PreferredWidth.HasValue)
        {
            table.Properties.Width = MathF.Max(0f, options.PreferredWidth.Value);
            table.Properties.WidthUnit = options.PreferredWidthUnit ?? TableWidthUnit.Dxa;
            updated = true;
        }

        if (options.Indent.HasValue)
        {
            table.Properties.Indent = MathF.Max(0f, options.Indent.Value);
            table.Properties.IndentUnit = options.IndentUnit ?? TableWidthUnit.Dxa;
            updated = true;
        }

        if (options.CellSpacing.HasValue)
        {
            table.Properties.CellSpacing = MathF.Max(0f, options.CellSpacing.Value);
            table.Properties.CellSpacingUnit = options.CellSpacingUnit ?? TableWidthUnit.Dxa;
            updated = true;
        }

        if (options.CellPadding.HasValue)
        {
            table.Properties.CellPadding = options.CellPadding;
            updated = true;
        }

        var selectionRange = GetSelectionRangeOrCurrent(context);
        if (options.RowHeight.HasValue
            || options.RowHeightRule.HasValue
            || options.CantSplit.HasValue
            || options.RepeatHeaderRows.HasValue)
        {
            ApplyRowProperties(selectionRange, options);
            updated = true;
        }

        if (options.ColumnWidth.HasValue)
        {
            ApplyColumnWidth(selectionRange, options.ColumnWidth.Value);
            updated = true;
        }

        if (options.CellVerticalAlignment.HasValue)
        {
            ApplyCellVerticalAlignment(selectionRange, options.CellVerticalAlignment.Value);
            updated = true;
        }

        if (updated)
        {
            _session.RefreshLayout();
        }
    }

    private void MergeCells()
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        range = ClampSelectionRange(range);
        if (range.RowStart == range.RowEnd && range.ColumnStart == range.ColumnEnd)
        {
            return;
        }

        var mergeWidth = range.ColumnEnd - range.ColumnStart + 1;
        var mergeHeight = range.RowEnd - range.RowStart + 1;
        for (var rowIndex = range.RowStart; rowIndex <= range.RowEnd; rowIndex++)
        {
            var row = range.Table.Rows[rowIndex];
            var originCell = MergeRowCells(row, range.ColumnStart, range.ColumnEnd);
            if (originCell is null)
            {
                continue;
            }

            if (mergeHeight > 1)
            {
                originCell.VerticalMerge = rowIndex == range.RowStart
                    ? TableCellVerticalMerge.Restart
                    : TableCellVerticalMerge.Continue;
            }
            else
            {
                originCell.VerticalMerge = TableCellVerticalMerge.None;
            }

            originCell.ColumnSpan = mergeWidth;
        }

        _session.RefreshLayout();
    }

    private void SplitCells()
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        range = ClampSelectionRange(range);
        var table = range.Table;

        for (var rowIndex = range.RowStart; rowIndex <= range.RowEnd; rowIndex++)
        {
            if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            {
                continue;
            }

            var row = table.Rows[rowIndex];
            var placements = GetRowPlacements(row);
            for (var placementIndex = placements.Count - 1; placementIndex >= 0; placementIndex--)
            {
                var placement = placements[placementIndex];
                if (!IntersectsColumns(placement, range.ColumnStart, range.ColumnEnd))
                {
                    continue;
                }

                var cell = placement.Cell;
                var originalSpan = Math.Max(1, cell.ColumnSpan);
                if (cell.VerticalMerge != TableCellVerticalMerge.None)
                {
                    ClearVerticalMerge(table, rowIndex, placement.Start, originalSpan);
                }

                if (originalSpan > 1)
                {
                    cell.ColumnSpan = 1;
                    for (var i = 1; i < originalSpan; i++)
                    {
                        row.Cells.Insert(placement.Index + i, CreateCellFromTemplate(cell));
                    }
                }
            }
        }

        _session.RefreshLayout();
    }

    private void AutoFitContents()
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        var properties = range.Table.Properties;
        properties.LayoutMode = TableLayoutMode.Auto;
        properties.ColumnWidths.Clear();
        properties.Width = null;
        properties.WidthUnit = null;
        _session.RefreshLayout();
    }

    private void AutoFitWindow()
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        var properties = range.Table.Properties;
        properties.LayoutMode = TableLayoutMode.Auto;
        properties.ColumnWidths.Clear();
        properties.Width = 100f;
        properties.WidthUnit = TableWidthUnit.Pct;
        _session.RefreshLayout();
    }

    private void FixedColumnWidth()
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        if (!TryGetTableLayout(range.Table, out var layout))
        {
            return;
        }

        var properties = range.Table.Properties;
        properties.LayoutMode = TableLayoutMode.Fixed;
        properties.ColumnWidths.Clear();
        properties.ColumnWidths.AddRange(layout.ColumnWidths);
        _session.RefreshLayout();
    }

    private void SetColumnWidths(object? payload)
    {
        if (payload is not EditorTableColumnWidthsRequest request)
        {
            return;
        }

        if (!TryGetTableContext(out var context))
        {
            return;
        }

        var widths = request.ColumnWidths.Span;
        if (widths.IsEmpty)
        {
            return;
        }

        var table = context.Table;
        var columnCount = GetColumnCount(table);
        if (columnCount <= 0)
        {
            return;
        }

        var properties = table.Properties;
        properties.LayoutMode = TableLayoutMode.Fixed;
        properties.ColumnWidths.Clear();

        var count = Math.Min(columnCount, widths.Length);
        for (var i = 0; i < count; i++)
        {
            properties.ColumnWidths.Add(MathF.Max(0f, widths[i]));
        }

        if (properties.ColumnWidths.Count == 0)
        {
            return;
        }

        if (properties.ColumnWidths.Count < columnCount)
        {
            var fillWidth = properties.ColumnWidths[^1];
            for (var i = properties.ColumnWidths.Count; i < columnCount; i++)
            {
                properties.ColumnWidths.Add(fillWidth);
            }
        }

        _session.RefreshLayout();
    }

    private void SetRowHeight(object? payload)
    {
        if (payload is not EditorTableRowHeightRequest request)
        {
            return;
        }

        if (!TryGetTableContext(out var context))
        {
            return;
        }

        var table = context.Table;
        if (table.Rows.Count == 0)
        {
            return;
        }

        var rowIndex = Math.Clamp(request.RowIndex, 0, table.Rows.Count - 1);
        var rowProperties = table.Rows[rowIndex].Properties;
        rowProperties.Height = MathF.Max(0f, request.Height);
        rowProperties.HeightRule = request.Rule;
        _session.RefreshLayout();
    }

    private void DistributeColumns()
    {
        if (!TryGetTableContext(out var context))
        {
            return;
        }

        var table = context.Table;
        var columnCount = GetColumnCount(table);
        if (columnCount <= 0)
        {
            return;
        }

        float width;
        if (TryGetTableLayout(table, out var layout))
        {
            width = layout.ColumnWidths.Sum() / columnCount;
        }
        else if (table.Properties.ColumnWidths.Count > 0)
        {
            width = table.Properties.ColumnWidths.Sum() / columnCount;
        }
        else
        {
            width = 80f;
        }

        table.Properties.LayoutMode = TableLayoutMode.Fixed;
        table.Properties.ColumnWidths.Clear();
        for (var i = 0; i < columnCount; i++)
        {
            table.Properties.ColumnWidths.Add(width);
        }

        _session.RefreshLayout();
    }

    private void DistributeRows()
    {
        if (!TryGetTableContext(out var context))
        {
            return;
        }

        var table = context.Table;
        if (table.Rows.Count == 0)
        {
            return;
        }

        float height;
        if (TryGetTableLayout(table, out var layout) && layout.RowHeights.Count > 0)
        {
            height = layout.RowHeights.Sum() / layout.RowHeights.Count;
        }
        else
        {
            height = MathF.Max(_session.Layout.LineHeight * 1.2f, 12f);
        }

        foreach (var row in table.Rows)
        {
            row.Properties.Height = height;
            row.Properties.HeightRule = TableRowHeightRule.Exact;
        }

        _session.RefreshLayout();
    }

    private void ApplyVerticalAlignment(TableCellVerticalAlignment alignment)
    {
        if (!TryGetSelectionRange(out var range))
        {
            return;
        }

        range = ClampSelectionRange(range);
        ApplyCellVerticalAlignment(range, alignment);
        _session.RefreshLayout();
    }

    private static void ApplyCellVerticalAlignment(TableSelectionRange range, TableCellVerticalAlignment alignment)
    {
        for (var rowIndex = range.RowStart; rowIndex <= range.RowEnd; rowIndex++)
        {
            var row = range.Table.Rows[rowIndex];
            foreach (var placement in GetRowPlacements(row))
            {
                if (placement.Start > range.ColumnEnd || placement.Start + placement.Span - 1 < range.ColumnStart)
                {
                    continue;
                }

                placement.Cell.Properties.VerticalAlignment = alignment;
            }
        }
    }

    private TableSelectionRange GetSelectionRangeOrCurrent(TableContext context)
    {
        if (TryGetSelectionRange(out var range))
        {
            return ClampSelectionRange(range);
        }

        var rowIndex = Math.Clamp(context.RowIndex, 0, Math.Max(0, context.Table.Rows.Count - 1));
        var columnCount = GetColumnCount(context.Table);
        var columnIndex = Math.Clamp(context.ColumnIndex, 0, Math.Max(0, columnCount - 1));
        return new TableSelectionRange(context.Table, rowIndex, rowIndex, columnIndex, columnIndex);
    }

    private static TableSelectionRange ClampSelectionRange(TableSelectionRange range)
    {
        var rowCount = Math.Max(1, range.Table.Rows.Count);
        var columnCount = Math.Max(1, GetColumnCount(range.Table));
        var rowStart = Math.Clamp(range.RowStart, 0, rowCount - 1);
        var rowEnd = Math.Clamp(range.RowEnd, 0, rowCount - 1);
        if (rowEnd < rowStart)
        {
            (rowStart, rowEnd) = (rowEnd, rowStart);
        }

        var columnStart = Math.Clamp(range.ColumnStart, 0, columnCount - 1);
        var columnEnd = Math.Clamp(range.ColumnEnd, 0, columnCount - 1);
        if (columnEnd < columnStart)
        {
            (columnStart, columnEnd) = (columnEnd, columnStart);
        }

        return range with
        {
            RowStart = rowStart,
            RowEnd = rowEnd,
            ColumnStart = columnStart,
            ColumnEnd = columnEnd
        };
    }

    private static void ApplyRowProperties(TableSelectionRange range, EditorTablePropertiesDialogOptions options)
    {
        var rowStart = Math.Clamp(range.RowStart, 0, Math.Max(0, range.Table.Rows.Count - 1));
        var rowEnd = Math.Clamp(range.RowEnd, 0, Math.Max(0, range.Table.Rows.Count - 1));
        if (rowEnd < rowStart)
        {
            (rowStart, rowEnd) = (rowEnd, rowStart);
        }

        for (var rowIndex = rowStart; rowIndex <= rowEnd; rowIndex++)
        {
            var rowProperties = range.Table.Rows[rowIndex].Properties;
            if (options.RowHeight.HasValue)
            {
                rowProperties.Height = MathF.Max(0f, options.RowHeight.Value);
            }

            if (options.RowHeightRule.HasValue)
            {
                rowProperties.HeightRule = options.RowHeightRule.Value;
            }

            if (options.CantSplit.HasValue)
            {
                rowProperties.CantSplit = options.CantSplit.Value;
            }

            if (options.RepeatHeaderRows.HasValue)
            {
                rowProperties.RepeatOnEachPage = options.RepeatHeaderRows.Value;
            }
        }
    }

    private void ApplyColumnWidth(TableSelectionRange range, float width)
    {
        var columnCount = GetColumnCount(range.Table);
        if (columnCount <= 0)
        {
            return;
        }

        var columnStart = Math.Clamp(range.ColumnStart, 0, columnCount - 1);
        var columnEnd = Math.Clamp(range.ColumnEnd, 0, columnCount - 1);
        if (columnEnd < columnStart)
        {
            (columnStart, columnEnd) = (columnEnd, columnStart);
        }

        var normalizedWidth = MathF.Max(0f, width);
        if (normalizedWidth <= 0f)
        {
            return;
        }

        var buffer = ArrayPool<float>.Shared.Rent(columnCount);
        var span = buffer.AsSpan(0, columnCount);
        try
        {
            if (TryGetTableLayout(range.Table, out var layout) && layout.ColumnWidths.Count > 0)
            {
                for (var i = 0; i < columnCount; i++)
                {
                    span[i] = i < layout.ColumnWidths.Count ? layout.ColumnWidths[i] : normalizedWidth;
                }
            }
            else if (range.Table.Properties.ColumnWidths.Count > 0)
            {
                var fallback = range.Table.Properties.ColumnWidths[^1];
                for (var i = 0; i < columnCount; i++)
                {
                    span[i] = i < range.Table.Properties.ColumnWidths.Count
                        ? range.Table.Properties.ColumnWidths[i]
                        : fallback;
                }
            }
            else
            {
                span.Fill(normalizedWidth);
            }

            for (var i = columnStart; i <= columnEnd; i++)
            {
                span[i] = normalizedWidth;
            }

            range.Table.Properties.LayoutMode = TableLayoutMode.Fixed;
            range.Table.Properties.ColumnWidths.Clear();
            for (var i = 0; i < columnCount; i++)
            {
                range.Table.Properties.ColumnWidths.Add(MathF.Max(0f, span[i]));
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    private static TableRow BuildRowFromTemplate(TableBlock table, TableRow templateRow, int insertIndex)
    {
        var newRow = new TableRow();
        CopyTableRowProperties(templateRow.Properties, newRow.Properties);

        var columnIndex = Math.Max(0, newRow.Properties.GridBefore ?? 0);
        foreach (var cell in templateRow.Cells)
        {
            var span = Math.Max(1, cell.ColumnSpan);
            var newCell = CreateCellFromTemplate(cell);
            newCell.ColumnSpan = span;
            newCell.VerticalMerge = ResolveVerticalMergeForInsert(table, insertIndex, columnIndex, span);
            newRow.Cells.Add(newCell);
            columnIndex += span;
        }

        if (newRow.Cells.Count == 0)
        {
            newRow.Cells.Add(CreateCellFromTemplate(null));
        }

        return newRow;
    }

    private static TableRow BuildEmptyRow(int columnCount)
    {
        var row = new TableRow();
        for (var i = 0; i < columnCount; i++)
        {
            row.Cells.Add(CreateCellFromTemplate(null));
        }

        return row;
    }

    private static void InsertColumnAt(TableBlock table, int insertIndex)
    {
        var columnCount = Math.Max(1, GetColumnCount(table));
        insertIndex = Math.Clamp(insertIndex, 0, columnCount);
        foreach (var row in table.Rows)
        {
            var inserted = false;
            var gridBefore = Math.Max(0, row.Properties.GridBefore ?? 0);
            var gridAfter = Math.Max(0, row.Properties.GridAfter ?? 0);
            var totalSpan = 0;
            foreach (var rowCell in row.Cells)
            {
                totalSpan += Math.Max(1, rowCell.ColumnSpan);
            }

            var cellRegionStart = gridBefore;
            var cellRegionEnd = gridBefore + totalSpan;
            if (insertIndex <= cellRegionStart)
            {
                row.Properties.GridBefore = gridBefore + 1;
                continue;
            }

            if (insertIndex >= cellRegionEnd)
            {
                row.Properties.GridAfter = gridAfter + 1;
                continue;
            }

            var columnCursor = gridBefore;
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = Math.Max(1, cell.ColumnSpan);
                var cellStart = columnCursor;
                var cellEnd = columnCursor + span;

                if (insertIndex > cellStart && insertIndex < cellEnd)
                {
                    cell.ColumnSpan = span + 1;
                    inserted = true;
                    break;
                }

                if (insertIndex == cellStart)
                {
                    row.Cells.Insert(cellIndex, CreateCellFromTemplate(cell));
                    inserted = true;
                    break;
                }

                if (insertIndex == cellEnd)
                {
                    row.Cells.Insert(cellIndex + 1, CreateCellFromTemplate(cell));
                    inserted = true;
                    break;
                }

                columnCursor = cellEnd;
            }

            if (!inserted)
            {
                row.Properties.GridAfter = gridAfter + 1;
            }
        }

        if (table.Properties.ColumnWidths.Count > 0)
        {
            var widthIndex = Math.Clamp(insertIndex, 0, table.Properties.ColumnWidths.Count);
            var sampleIndex = Math.Clamp(widthIndex - 1, 0, table.Properties.ColumnWidths.Count - 1);
            var width = table.Properties.ColumnWidths.Count == 0 ? 0f : table.Properties.ColumnWidths[sampleIndex];
            table.Properties.ColumnWidths.Insert(widthIndex, width);
        }
    }

    private static void RemoveColumnAt(TableBlock table, int columnIndex)
    {
        var columnCount = GetColumnCount(table);
        if (columnCount <= 1)
        {
            return;
        }

        columnIndex = Math.Clamp(columnIndex, 0, columnCount - 1);
        foreach (var row in table.Rows)
        {
            var gridBefore = Math.Max(0, row.Properties.GridBefore ?? 0);
            var gridAfter = Math.Max(0, row.Properties.GridAfter ?? 0);
            var totalSpan = 0;
            foreach (var rowCell in row.Cells)
            {
                totalSpan += Math.Max(1, rowCell.ColumnSpan);
            }

            var cellRegionStart = gridBefore;
            var cellRegionEnd = gridBefore + totalSpan;
            if (columnIndex < cellRegionStart)
            {
                row.Properties.GridBefore = Math.Max(0, gridBefore - 1);
                continue;
            }

            if (columnIndex >= cellRegionEnd)
            {
                row.Properties.GridAfter = Math.Max(0, gridAfter - 1);
                continue;
            }

            if (!TryGetCellAtColumn(row, columnIndex, out var cell, out var cellIndex, out _))
            {
                continue;
            }

            if (cell.ColumnSpan > 1)
            {
                cell.ColumnSpan = Math.Max(1, cell.ColumnSpan - 1);
            }
            else
            {
                row.Cells.RemoveAt(cellIndex);
                if (row.Cells.Count == 0)
                {
                    row.Cells.Add(CreateCellFromTemplate(null));
                }
            }
        }

        if (table.Properties.ColumnWidths.Count > columnIndex)
        {
            table.Properties.ColumnWidths.RemoveAt(columnIndex);
        }
    }

    private static TableCell? MergeRowCells(TableRow row, int columnStart, int columnEnd)
    {
        var placements = GetRowPlacements(row);
        if (placements.Count == 0)
        {
            return null;
        }

        var originIndex = placements.FindIndex(p => columnStart >= p.Start && columnStart < p.Start + p.Span);
        if (originIndex < 0)
        {
            return null;
        }

        var origin = placements[originIndex];
        for (var i = placements.Count - 1; i >= 0; i--)
        {
            var placement = placements[i];
            if (placement.Index == origin.Index)
            {
                continue;
            }

            if (placement.Start >= columnStart && placement.Start <= columnEnd)
            {
                AppendCellContent(origin.Cell, placement.Cell);
                row.Cells.RemoveAt(placement.Index);
            }
        }

        return origin.Cell;
    }

    private static void AppendCellContent(TableCell target, TableCell source)
    {
        if (source.Paragraphs.Count == 0)
        {
            return;
        }

        if (target.Paragraphs.Count > 0)
        {
            target.Paragraphs.Add(new ParagraphBlock());
        }

        target.Paragraphs.AddRange(source.Paragraphs);
    }

    private static void ClearVerticalMerge(TableBlock table, int rowIndex, int columnStart, int columnSpan)
    {
        var topRow = rowIndex;
        while (topRow > 0 && TryGetCellAtColumn(table.Rows[topRow], columnStart, out var above, out _, out _)
               && above.VerticalMerge == TableCellVerticalMerge.Continue
               && above.ColumnSpan == columnSpan)
        {
            topRow--;
        }

        for (var row = topRow; row < table.Rows.Count; row++)
        {
            if (!TryGetCellAtColumn(table.Rows[row], columnStart, out var cell, out _, out _))
            {
                break;
            }

            if (cell.ColumnSpan != columnSpan)
            {
                break;
            }

            if (cell.VerticalMerge == TableCellVerticalMerge.None)
            {
                break;
            }

            cell.VerticalMerge = TableCellVerticalMerge.None;
        }
    }

    private static void NormalizeVerticalMerges(TableBlock table)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            foreach (var placement in GetRowPlacements(row))
            {
                if (placement.Cell.VerticalMerge != TableCellVerticalMerge.Continue)
                {
                    continue;
                }

                if (rowIndex == 0)
                {
                    placement.Cell.VerticalMerge = TableCellVerticalMerge.Restart;
                    continue;
                }

                if (!TryGetCellAtColumn(table.Rows[rowIndex - 1], placement.Start, out var above, out _, out _)
                    || above.VerticalMerge == TableCellVerticalMerge.None
                    || above.ColumnSpan != placement.Span)
                {
                    placement.Cell.VerticalMerge = TableCellVerticalMerge.Restart;
                }
            }
        }
    }

    private static bool TryGetCellAtColumn(
        TableRow row,
        int columnIndex,
        out TableCell cell,
        out int cellIndex,
        out int cellStart)
    {
        var cursor = Math.Max(0, row.Properties.GridBefore ?? 0);
        for (var i = 0; i < row.Cells.Count; i++)
        {
            var current = row.Cells[i];
            var span = Math.Max(1, current.ColumnSpan);
            if (columnIndex >= cursor && columnIndex < cursor + span)
            {
                cell = current;
                cellIndex = i;
                cellStart = cursor;
                return true;
            }

            cursor += span;
        }

        cell = null!;
        cellIndex = -1;
        cellStart = -1;
        return false;
    }

    private static int GetColumnCount(TableBlock table)
    {
        var max = 0;
        foreach (var row in table.Rows)
        {
            var count = Math.Max(0, row.Properties.GridBefore ?? 0);
            foreach (var cell in row.Cells)
            {
                count += Math.Max(1, cell.ColumnSpan);
            }

            count += Math.Max(0, row.Properties.GridAfter ?? 0);
            max = Math.Max(max, count);
        }

        return Math.Max(max, 1);
    }

    private static List<CellPlacement> GetRowPlacements(TableRow row)
    {
        var placements = new List<CellPlacement>();
        var cursor = Math.Max(0, row.Properties.GridBefore ?? 0);
        for (var i = 0; i < row.Cells.Count; i++)
        {
            var cell = row.Cells[i];
            var span = Math.Max(1, cell.ColumnSpan);
            placements.Add(new CellPlacement(cell, i, cursor, span));
            cursor += span;
        }

        return placements;
    }

    private static bool IntersectsColumns(CellPlacement placement, int columnStart, int columnEnd)
    {
        var start = placement.Start;
        var end = placement.Start + placement.Span - 1;
        return end >= columnStart && start <= columnEnd;
    }

    private static TableCell CreateCellFromTemplate(TableCell? template)
    {
        var cell = new TableCell();
        cell.Paragraphs.Add(new ParagraphBlock());
        if (template is null)
        {
            return cell;
        }

        CopyTableCellProperties(template.Properties, cell.Properties);
        return cell;
    }

    private static TableCellVerticalMerge ResolveVerticalMergeForInsert(TableBlock table, int insertIndex, int columnIndex, int columnSpan)
    {
        if (insertIndex < 0 || insertIndex >= table.Rows.Count)
        {
            return TableCellVerticalMerge.None;
        }

        if (!TryGetCellAtColumn(table.Rows[insertIndex], columnIndex, out var below, out _, out _))
        {
            return TableCellVerticalMerge.None;
        }

        if (below.ColumnSpan != columnSpan)
        {
            return TableCellVerticalMerge.None;
        }

        return below.VerticalMerge == TableCellVerticalMerge.Continue
            ? TableCellVerticalMerge.Continue
            : TableCellVerticalMerge.None;
    }

    private static void CopyTableRowProperties(TableRowProperties source, TableRowProperties target)
    {
        target.Height = source.Height;
        target.HeightRule = source.HeightRule;
        target.CantSplit = source.CantSplit;
        target.RepeatOnEachPage = source.RepeatOnEachPage;
        target.ShadingColor = source.ShadingColor;
        target.GridBefore = source.GridBefore;
        target.GridAfter = source.GridAfter;
    }

    private static void CopyTableCellProperties(TableCellProperties source, TableCellProperties target)
    {
        target.Padding = source.Padding;
        target.ShadingColor = source.ShadingColor;
        target.VerticalAlignment = source.VerticalAlignment;
        target.TextDirection = source.TextDirection;
        target.Borders.Top = source.Borders.Top?.Clone();
        target.Borders.Bottom = source.Borders.Bottom?.Clone();
        target.Borders.Left = source.Borders.Left?.Clone();
        target.Borders.Right = source.Borders.Right?.Clone();
    }

    private static int FindParagraphIndexForBlock(Document document, int blockIndex)
    {
        var paragraphIndex = 0;
        for (var i = 0; i < document.Blocks.Count && i < blockIndex; i++)
        {
            switch (document.Blocks[i])
            {
                case ParagraphBlock:
                    paragraphIndex++;
                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            paragraphIndex += cell.Paragraphs.Count;
                        }
                    }

                    break;
            }
        }

        return paragraphIndex;
    }

    private bool TryGetTableLayout(TableBlock table, out TableLayout layout)
    {
        layout = null!;
        var tableIndex = 0;
        foreach (var block in _session.Document.Blocks)
        {
            if (block is not TableBlock candidate)
            {
                continue;
            }

            if (ReferenceEquals(candidate, table))
            {
                if (_session.Layout.Tables.Count > tableIndex)
                {
                    layout = _session.Layout.Tables[tableIndex];
                    return true;
                }

                return false;
            }

            tableIndex++;
        }

        return false;
    }

    private readonly record struct TableContext(TableBlock Table, int BlockIndex, int RowIndex, int ColumnIndex);

    private readonly record struct CellPlacement(TableCell Cell, int Index, int Start, int Span);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Vibe.Office.Documents;
using Vibe.Office.Editing;
using Vibe.Office.Ribbon;

namespace Vibe.Word.App;

internal sealed class EquationRibbonExtension : IRibbonExtension
{
    private readonly Func<bool> _isActive;
    private readonly Func<ValueTask> _refreshLayout;
    private readonly Func<bool>? _canExecute;

    public EquationRibbonExtension(Func<bool> isActive, Func<ValueTask> refreshLayout, Func<bool>? canExecute = null)
    {
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        _refreshLayout = refreshLayout ?? throw new ArgumentNullException(nameof(refreshLayout));
        _canExecute = canExecute;
    }

    public void Build(RibbonModelBuilder builder, RibbonExtensionContext context)
    {
        var contextualSet = new RibbonContextualTabSet(
            "equation-tools",
            "Equation Tools",
            _isActive,
            accentKey: "Equation");
        builder.AddContextualSet(contextualSet);

        var equationGroup = new RibbonGroup(
            "equation",
            "Equation",
            new IRibbonControl[]
            {
                new RibbonButton(
                    "equation-refresh",
                    "Refresh Layout",
                    new RibbonCommand(_refreshLayout, _canExecute),
                    iconKey: "RibbonIcon.Equation",
                    size: RibbonControlSize.Medium)
            },
            keyTip: "EQ");

        builder.AddTab(
                "equation-design",
                "Design",
                keyTip: "E",
                contextualSet: contextualSet)
            .AddGroup(equationGroup);
    }
}

internal sealed class HeaderFooterRibbonExtension : IRibbonExtension
{
    private readonly Func<bool>? _canExecute;

    public HeaderFooterRibbonExtension(Func<bool>? canExecute = null)
    {
        _canExecute = canExecute;
    }

    public void Build(RibbonModelBuilder builder, RibbonExtensionContext context)
    {
        if (!context.TryGetService<IHeaderFooterEditService>(out var headerFooterService))
        {
            return;
        }

        bool CanExecute()
        {
            return (_canExecute?.Invoke() ?? true) && headerFooterService.IsEditing;
        }

        bool CanGoPrevious()
        {
            return CanExecute() && headerFooterService.SectionIndex > 0;
        }

        bool CanGoNext()
        {
            return CanExecute() && headerFooterService.SectionIndex < Math.Max(0, headerFooterService.SectionCount - 1);
        }

        var contextualSet = new RibbonContextualTabSet(
            "header-footer-tools",
            "Header & Footer Tools",
            () => headerFooterService.IsEditing,
            accentKey: "Text");
        builder.AddContextualSet(contextualSet);

        RibbonCommand CreateCommand(Action action, Func<bool>? canExecute = null)
        {
            return new RibbonCommand(action, canExecute ?? CanExecute);
        }

        var previousSectionButton = new RibbonButton(
            "header-footer-previous-section",
            "Previous Section",
            CreateCommand(headerFooterService.GoToPreviousSection, CanGoPrevious),
            iconKey: "RibbonIcon.Layout",
            size: RibbonControlSize.Small);

        var nextSectionButton = new RibbonButton(
            "header-footer-next-section",
            "Next Section",
            CreateCommand(headerFooterService.GoToNextSection, CanGoNext),
            iconKey: "RibbonIcon.Layout",
            size: RibbonControlSize.Small);

        var goToHeaderButton = new RibbonButton(
            "header-footer-go-header",
            "Go to Header",
            CreateCommand(headerFooterService.BeginHeader),
            iconKey: "RibbonIcon.Header",
            size: RibbonControlSize.Medium);

        var goToFooterButton = new RibbonButton(
            "header-footer-go-footer",
            "Go to Footer",
            CreateCommand(headerFooterService.BeginFooter),
            iconKey: "RibbonIcon.Footer",
            size: RibbonControlSize.Medium);

        var closeButton = new RibbonButton(
            "header-footer-close",
            "Close Header and Footer",
            CreateCommand(headerFooterService.Close),
            iconKey: "RibbonIcon.Check",
            size: RibbonControlSize.Small);

        var navigationGroup = new RibbonGroup(
            "header-footer-navigation",
            "Navigation",
            new IRibbonControl[]
            {
                previousSectionButton,
                nextSectionButton,
                goToHeaderButton,
                goToFooterButton,
                closeButton
            },
            keyTip: "HN");

        var differentFirstToggle = new RibbonToggleButton(
            "header-footer-different-first",
            "Different First Page",
            () => headerFooterService.DifferentFirstPage,
            toggleHandler: isChecked =>
            {
                headerFooterService.DifferentFirstPage = isChecked;
                return ValueTask.CompletedTask;
            },
            iconKey: "RibbonIcon.BlankPage",
            size: RibbonControlSize.Small,
            canExecute: CanExecute);

        var differentOddEvenToggle = new RibbonToggleButton(
            "header-footer-different-odd-even",
            "Different Odd & Even",
            () => headerFooterService.DifferentOddEven,
            toggleHandler: isChecked =>
            {
                headerFooterService.DifferentOddEven = isChecked;
                return ValueTask.CompletedTask;
            },
            iconKey: "RibbonIcon.PageNumber",
            size: RibbonControlSize.Small,
            canExecute: CanExecute);

        var optionsGroup = new RibbonGroup(
            "header-footer-options",
            "Options",
            new IRibbonControl[]
            {
                differentFirstToggle,
                differentOddEvenToggle
            },
            keyTip: "HO");

        var variantMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuToggleItem(
                "header-footer-variant-default",
                "Default",
                () => headerFooterService.Variant == HeaderFooterVariant.Default,
                new RibbonCommand(() => headerFooterService.SetVariant(HeaderFooterVariant.Default), CanExecute),
                iconKey: "RibbonIcon.Header"),
            new RibbonMenuToggleItem(
                "header-footer-variant-first",
                "First Page",
                () => headerFooterService.Variant == HeaderFooterVariant.First,
                new RibbonCommand(() => headerFooterService.SetVariant(HeaderFooterVariant.First), CanExecute),
                iconKey: "RibbonIcon.BlankPage"),
            new RibbonMenuToggleItem(
                "header-footer-variant-even",
                "Even Pages",
                () => headerFooterService.Variant == HeaderFooterVariant.Even,
                new RibbonCommand(() => headerFooterService.SetVariant(HeaderFooterVariant.Even), CanExecute),
                iconKey: "RibbonIcon.PageNumber")
        });

        var variantButton = new RibbonDropdownButton(
            "header-footer-variant",
            "Header Type",
            variantMenu,
            iconKey: "RibbonIcon.Header",
            size: RibbonControlSize.Medium,
            canExecute: CanExecute);

        var variantGroup = new RibbonGroup(
            "header-footer-variant-group",
            "Type",
            new IRibbonControl[]
            {
                variantButton
            },
            keyTip: "HT");

        builder.AddTab("header-footer-design", "Design", keyTip: "HF", contextualSet: contextualSet)
            .AddGroups(new[]
            {
                navigationGroup,
                optionsGroup,
                variantGroup
            });
    }
}

internal sealed class TableRibbonExtension : IRibbonExtension
{
    private readonly Func<ValueTask>? _openPropertiesDialog;

    public TableRibbonExtension(Func<ValueTask>? openPropertiesDialog = null)
    {
        _openPropertiesDialog = openPropertiesDialog;
    }

    public void Build(RibbonModelBuilder builder, RibbonExtensionContext context)
    {
        if (!context.TryGetService<IRibbonContextSnapshotProvider>(out var snapshotProvider))
        {
            return;
        }

        if (!context.TryGetService<IEditorCommandRouter>(out var commandRouter))
        {
            return;
        }

        context.TryGetService<ITableStyleService>(out var tableStyleService);
        context.TryGetService<ITableSelectionSnapshotProvider>(out var tableSelectionProvider);

        bool IsActive()
        {
            var snapshot = snapshotProvider.GetSnapshot();
            return snapshot.Selection.IsInTable;
        }

        bool CanExecute(string commandId)
        {
            var snapshot = snapshotProvider.GetSnapshot();
            return commandRouter.CanExecute(commandId, null, snapshot);
        }

        ValueTask ExecuteAsync(string commandId, object? payload = null)
        {
            var snapshot = snapshotProvider.GetSnapshot();
            _ = commandRouter.ExecuteAsync(commandId, payload, snapshot);
            return ValueTask.CompletedTask;
        }

        RibbonCommand CreateCommand(string commandId)
        {
            return new RibbonCommand(() => ExecuteAsync(commandId), () => CanExecute(commandId));
        }

        bool TryGetTableSelection(out EditorTableSelectionSnapshot selection)
        {
            selection = default;
            return tableSelectionProvider is not null
                   && tableSelectionProvider.TryGetSnapshot(out selection);
        }

        bool IsTableLookValue(Func<TableLook, bool> selector)
        {
            if (!TryGetTableSelection(out var selection))
            {
                return false;
            }

            var look = selection.Table.Properties.Look ?? new TableLook();
            return selector(look);
        }

        ValueTask ExecuteLookCommandAsync(string commandId, bool value)
        {
            return ExecuteAsync(commandId, value);
        }

        static bool TryResolveColumnWidths(EditorTableSelectionSnapshot selection, out IReadOnlyList<float> widths)
        {
            widths = selection.Table.Properties.ColumnWidths;
            if (widths.Count > 0 && selection.Table.Properties.LayoutMode == TableLayoutMode.Fixed)
            {
                return true;
            }

            if (selection.Layout is { } layout && layout.ColumnWidths.Count > 0)
            {
                widths = layout.ColumnWidths;
                return true;
            }

            return widths.Count > 0;
        }

        double? ResolveColumnWidthPoints()
        {
            if (!TryGetTableSelection(out var selection))
            {
                return null;
            }

            if (!TryResolveColumnWidths(selection, out var widths) || widths.Count == 0)
            {
                return null;
            }

            var start = Math.Clamp(selection.ColumnStart, 0, widths.Count - 1);
            var end = Math.Clamp(selection.ColumnEnd, 0, widths.Count - 1);
            if (end < start)
            {
                (start, end) = (end, start);
            }

            var first = widths[start];
            for (var i = start + 1; i <= end; i++)
            {
                if (MathF.Abs(widths[i] - first) > 0.01f)
                {
                    return null;
                }
            }

            return DipsToPoints(first);
        }

        ValueTask ApplyColumnWidthAsync(double? points)
        {
            if (!points.HasValue)
            {
                return ValueTask.CompletedTask;
            }

            if (!TryGetTableSelection(out var selection))
            {
                return ValueTask.CompletedTask;
            }

            if (!TryResolveColumnWidths(selection, out var widths) || widths.Count == 0)
            {
                return ValueTask.CompletedTask;
            }

            var updated = new float[widths.Count];
            for (var i = 0; i < widths.Count; i++)
            {
                updated[i] = widths[i];
            }

            var start = Math.Clamp(selection.ColumnStart, 0, updated.Length - 1);
            var end = Math.Clamp(selection.ColumnEnd, 0, updated.Length - 1);
            if (end < start)
            {
                (start, end) = (end, start);
            }

            var newWidth = PointsToDips(points.Value);
            for (var i = start; i <= end; i++)
            {
                updated[i] = newWidth;
            }

            return ExecuteAsync(EditorTableCommandIds.Layout.ColumnWidthsSet, new EditorTableColumnWidthsRequest(updated));
        }

        double? ResolveRowHeightPoints()
        {
            if (!TryGetTableSelection(out var selection))
            {
                return null;
            }

            var table = selection.Table;
            if (table.Rows.Count == 0)
            {
                return null;
            }

            var start = Math.Clamp(selection.RowStart, 0, table.Rows.Count - 1);
            var end = Math.Clamp(selection.RowEnd, 0, table.Rows.Count - 1);
            float? height = null;
            TableRowHeightRule? rule = null;

            for (var i = start; i <= end; i++)
            {
                var properties = table.Rows[i].Properties;
                if (!properties.Height.HasValue)
                {
                    height = null;
                    break;
                }

                if (!height.HasValue)
                {
                    height = properties.Height.Value;
                    rule = properties.HeightRule;
                    continue;
                }

                if (MathF.Abs(properties.Height.Value - height.Value) > 0.01f
                    || properties.HeightRule != rule)
                {
                    return null;
                }
            }

            if (height.HasValue)
            {
                return DipsToPoints(height.Value);
            }

            if (start == end
                && selection.Layout is { } layout
                && selection.RowIndex >= 0
                && selection.RowIndex < layout.RowHeights.Count)
            {
                return DipsToPoints(layout.RowHeights[selection.RowIndex]);
            }

            return null;
        }

        ValueTask ApplyRowHeightAsync(double? points)
        {
            if (!points.HasValue)
            {
                return ValueTask.CompletedTask;
            }

            if (!TryGetTableSelection(out var selection))
            {
                return ValueTask.CompletedTask;
            }

            var table = selection.Table;
            if (table.Rows.Count == 0)
            {
                return ValueTask.CompletedTask;
            }

            var rowIndex = Math.Clamp(selection.RowIndex, 0, table.Rows.Count - 1);
            var rule = table.Rows[rowIndex].Properties.HeightRule ?? TableRowHeightRule.AtLeast;
            var height = PointsToDips(points.Value);

            return ExecuteAsync(EditorTableCommandIds.Layout.RowHeightSet, new EditorTableRowHeightRequest(rowIndex, height, rule));
        }

        var contextualSet = new RibbonContextualTabSet(
            "table-tools",
            "Table Tools",
            IsActive,
            accentKey: "Table");
        builder.AddContextualSet(contextualSet);

        var insertRowAbove = new RibbonButton(
            "table-row-above",
            "Insert Above",
            CreateCommand(EditorTableCommandIds.Rows.InsertAbove),
            iconKey: "RibbonIcon.Table",
            size: RibbonControlSize.Small);

        var insertRowBelow = new RibbonButton(
            "table-row-below",
            "Insert Below",
            CreateCommand(EditorTableCommandIds.Rows.InsertBelow),
            iconKey: "RibbonIcon.Table",
            size: RibbonControlSize.Small);

        var insertColumnLeft = new RibbonButton(
            "table-col-left",
            "Insert Left",
            CreateCommand(EditorTableCommandIds.Columns.InsertLeft),
            iconKey: "RibbonIcon.Table",
            size: RibbonControlSize.Small);

        var insertColumnRight = new RibbonButton(
            "table-col-right",
            "Insert Right",
            CreateCommand(EditorTableCommandIds.Columns.InsertRight),
            iconKey: "RibbonIcon.Table",
            size: RibbonControlSize.Small);

        var deleteMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "table-delete-row",
                "Delete Row",
                CreateCommand(EditorTableCommandIds.Rows.Delete),
                iconKey: "RibbonIcon.Cut"),
            new RibbonMenuItem(
                "table-delete-column",
                "Delete Column",
                CreateCommand(EditorTableCommandIds.Columns.Delete),
                iconKey: "RibbonIcon.Cut"),
            new RibbonMenuItem(
                "table-delete-table",
                "Delete Table",
                CreateCommand(EditorTableCommandIds.Delete.Table),
                iconKey: "RibbonIcon.Cut")
        });

        var deleteButton = new RibbonDropdownButton(
            "table-delete",
            "Delete",
            deleteMenu,
            iconKey: "RibbonIcon.Cut",
            size: RibbonControlSize.Small);

        var rowsColumnsGroup = new RibbonGroup(
            "table-rows-columns",
            "Rows & Columns",
            new IRibbonControl[]
            {
                insertRowAbove,
                insertRowBelow,
                insertColumnLeft,
                insertColumnRight,
                deleteButton
            },
            keyTip: "RC");

        var mergeCells = new RibbonButton(
            "table-merge",
            "Merge Cells",
            CreateCommand(EditorTableCommandIds.Merge.Cells),
            iconKey: "RibbonIcon.Borders",
            size: RibbonControlSize.Small);

        var splitCells = new RibbonButton(
            "table-split",
            "Split Cells",
            CreateCommand(EditorTableCommandIds.Merge.Split),
            iconKey: "RibbonIcon.Borders",
            size: RibbonControlSize.Small);

        var mergeGroup = new RibbonGroup(
            "table-merge-group",
            "Merge",
            new IRibbonControl[]
            {
                mergeCells,
                splitCells
            },
            keyTip: "MG");

        var autoFitMenu = new RibbonMenu(new IRibbonMenuEntry[]
        {
            new RibbonMenuItem(
                "table-autofit-contents",
                "AutoFit Contents",
                CreateCommand(EditorTableCommandIds.Layout.AutoFitContents),
                iconKey: "RibbonIcon.Layout"),
            new RibbonMenuItem(
                "table-autofit-window",
                "AutoFit Window",
                CreateCommand(EditorTableCommandIds.Layout.AutoFitWindow),
                iconKey: "RibbonIcon.Layout"),
            new RibbonMenuItem(
                "table-fixed-columns",
                "Fixed Column Width",
                CreateCommand(EditorTableCommandIds.Layout.FixedColumnWidth),
                iconKey: "RibbonIcon.Layout")
        });

        var autoFitButton = new RibbonDropdownButton(
            "table-autofit",
            "AutoFit",
            autoFitMenu,
            iconKey: "RibbonIcon.Layout",
            size: RibbonControlSize.Small);

        var distributeColumns = new RibbonButton(
            "table-distribute-columns",
            "Distribute Columns",
            CreateCommand(EditorTableCommandIds.Layout.DistributeColumns),
            iconKey: "RibbonIcon.Layout",
            size: RibbonControlSize.Small);

        var distributeRows = new RibbonButton(
            "table-distribute-rows",
            "Distribute Rows",
            CreateCommand(EditorTableCommandIds.Layout.DistributeRows),
            iconKey: "RibbonIcon.Layout",
            size: RibbonControlSize.Small);

        var repeatHeaderRows = new RibbonButton(
            "table-repeat-header",
            "Repeat Header Rows",
            CreateCommand(EditorTableCommandIds.Layout.RepeatHeaderRows),
            iconKey: "RibbonIcon.Table",
            size: RibbonControlSize.Small);

        var layoutGroup = new RibbonGroup(
            "table-layout-group",
            "Layout",
            new IRibbonControl[]
            {
                autoFitButton,
                distributeColumns,
                distributeRows,
                repeatHeaderRows
            },
            keyTip: "LY");

        var cellWidthSpinner = new RibbonSpinner(
            "table-cell-width",
            "Width",
            step: 1d,
            minimum: 0d,
            valueEvaluator: ResolveColumnWidthPoints,
            valueChangedHandler: ApplyColumnWidthAsync,
            keyTip: "CW",
            iconKey: "RibbonIcon.Layout",
            canExecute: () => CanExecute(EditorTableCommandIds.Layout.ColumnWidthsSet),
            size: RibbonControlSize.Medium);

        var cellHeightSpinner = new RibbonSpinner(
            "table-cell-height",
            "Height",
            step: 1d,
            minimum: 0d,
            valueEvaluator: ResolveRowHeightPoints,
            valueChangedHandler: ApplyRowHeightAsync,
            keyTip: "CH",
            iconKey: "RibbonIcon.Layout",
            canExecute: () => CanExecute(EditorTableCommandIds.Layout.RowHeightSet),
            size: RibbonControlSize.Medium);

        var cellSizeGroup = new RibbonGroup(
            "table-cell-size",
            "Cell Size",
            new IRibbonControl[]
            {
                cellWidthSpinner,
                cellHeightSpinner
            },
            keyTip: "CS");

        var alignTop = new RibbonButton(
            "table-align-top",
            "Align Top",
            CreateCommand(EditorTableCommandIds.Alignment.AlignTop),
            iconKey: "RibbonIcon.AlignLeft",
            size: RibbonControlSize.Small);

        var alignMiddle = new RibbonButton(
            "table-align-middle",
            "Align Middle",
            CreateCommand(EditorTableCommandIds.Alignment.AlignMiddle),
            iconKey: "RibbonIcon.AlignCenter",
            size: RibbonControlSize.Small);

        var alignBottom = new RibbonButton(
            "table-align-bottom",
            "Align Bottom",
            CreateCommand(EditorTableCommandIds.Alignment.AlignBottom),
            iconKey: "RibbonIcon.AlignRight",
            size: RibbonControlSize.Small);

        var alignmentGroup = new RibbonGroup(
            "table-alignment",
            "Alignment",
            new IRibbonControl[]
            {
                alignTop,
                alignMiddle,
                alignBottom
            },
            keyTip: "AL");

        builder.AddTab(
                "table-layout",
                "Layout",
                keyTip: "T",
                contextualSet: contextualSet)
            .AddGroups(BuildLayoutGroups(
                rowsColumnsGroup,
                mergeGroup,
                cellSizeGroup,
                layoutGroup,
                alignmentGroup,
                () => CanExecute(EditorTableCommandIds.Layout.PropertiesApply)));

        var headerRowToggle = new RibbonToggleButton(
            "table-style-header-row",
            "Header Row",
            () => IsTableLookValue(look => look.FirstRow),
            toggleHandler: value => ExecuteLookCommandAsync(EditorTableCommandIds.Design.ToggleHeaderRow, value),
            keyTip: "HR",
            iconKey: "RibbonIcon.Table",
            canExecute: () => CanExecute(EditorTableCommandIds.Design.ToggleHeaderRow),
            size: RibbonControlSize.Small);

        var totalRowToggle = new RibbonToggleButton(
            "table-style-total-row",
            "Total Row",
            () => IsTableLookValue(look => look.LastRow),
            toggleHandler: value => ExecuteLookCommandAsync(EditorTableCommandIds.Design.ToggleTotalRow, value),
            keyTip: "TR",
            iconKey: "RibbonIcon.Table",
            canExecute: () => CanExecute(EditorTableCommandIds.Design.ToggleTotalRow),
            size: RibbonControlSize.Small);

        var bandedRowsToggle = new RibbonToggleButton(
            "table-style-banded-rows",
            "Banded Rows",
            () => IsTableLookValue(look => look.BandedRows),
            toggleHandler: value => ExecuteLookCommandAsync(EditorTableCommandIds.Design.ToggleBandedRows, value),
            keyTip: "BR",
            iconKey: "RibbonIcon.Table",
            canExecute: () => CanExecute(EditorTableCommandIds.Design.ToggleBandedRows),
            size: RibbonControlSize.Small);

        var firstColumnToggle = new RibbonToggleButton(
            "table-style-first-column",
            "First Column",
            () => IsTableLookValue(look => look.FirstColumn),
            toggleHandler: value => ExecuteLookCommandAsync(EditorTableCommandIds.Design.ToggleFirstColumn, value),
            keyTip: "FC",
            iconKey: "RibbonIcon.Table",
            canExecute: () => CanExecute(EditorTableCommandIds.Design.ToggleFirstColumn),
            size: RibbonControlSize.Small);

        var lastColumnToggle = new RibbonToggleButton(
            "table-style-last-column",
            "Last Column",
            () => IsTableLookValue(look => look.LastColumn),
            toggleHandler: value => ExecuteLookCommandAsync(EditorTableCommandIds.Design.ToggleLastColumn, value),
            keyTip: "LC",
            iconKey: "RibbonIcon.Table",
            canExecute: () => CanExecute(EditorTableCommandIds.Design.ToggleLastColumn),
            size: RibbonControlSize.Small);

        var bandedColumnsToggle = new RibbonToggleButton(
            "table-style-banded-columns",
            "Banded Columns",
            () => IsTableLookValue(look => look.BandedColumns),
            toggleHandler: value => ExecuteLookCommandAsync(EditorTableCommandIds.Design.ToggleBandedColumns, value),
            keyTip: "BC",
            iconKey: "RibbonIcon.Table",
            canExecute: () => CanExecute(EditorTableCommandIds.Design.ToggleBandedColumns),
            size: RibbonControlSize.Small);

        var styleOptionsGroup = new RibbonGroup(
            "table-style-options",
            "Table Style Options",
            new IRibbonControl[]
            {
                headerRowToggle,
                totalRowToggle,
                bandedRowsToggle,
                firstColumnToggle,
                lastColumnToggle,
                bandedColumnsToggle
            },
            keyTip: "TO");

        var designGroups = new List<RibbonGroup>
        {
            styleOptionsGroup
        };

        if (tableStyleService is not null)
        {
            var styleItems = BuildTableStyleItems(tableStyleService);
            var styleGallery = new RibbonGallery(
                "table-styles",
                "Table Styles",
                styleItems,
                selectedItemEvaluator: () => ResolveSelectedStyleItem(styleItems, tableStyleService),
                selectionHandler: item => ApplyTableStyleAsync(item, snapshotProvider, commandRouter),
                showDropDown: true,
                keyTip: "TS",
                iconKey: "RibbonIcon.Table",
                size: RibbonControlSize.Large);

            var stylesGroup = new RibbonGroup(
                "table-styles-group",
                "Table Styles",
                new IRibbonControl[] { styleGallery },
                keyTip: "TS");

            designGroups.Add(stylesGroup);
        }

        builder.AddTab(
                "table-design",
                "Design",
                keyTip: "D",
                contextualSet: contextualSet)
            .AddGroups(designGroups);
    }

    private const float PointsToDipScale = 96f / 72f;
    private static float PointsToDips(double points) => (float)(points * PointsToDipScale);
    private static double DipsToPoints(float dips) => dips / PointsToDipScale;

    private static List<RibbonGalleryItem> BuildTableStyleItems(ITableStyleService tableStyleService)
    {
        var styles = tableStyleService.GetTableStyles();
        var items = new List<RibbonGalleryItem>();
        items.Add(new RibbonGalleryItem("none", "No Style"));
        foreach (var style in styles)
        {
            items.Add(new RibbonGalleryItem(style.Id, style.Name));
        }

        return items;
    }

    private static RibbonGalleryItem? ResolveSelectedStyleItem(
        IReadOnlyList<RibbonGalleryItem> items,
        ITableStyleService tableStyleService)
    {
        var currentId = tableStyleService.GetCurrentTableStyleId();
        if (string.IsNullOrWhiteSpace(currentId))
        {
            return items.FirstOrDefault(item => item.Id == "none");
        }

        foreach (var item in items)
        {
            if (string.Equals(item.Id, currentId, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return items.FirstOrDefault(item => item.Id == "none");
    }

    private static ValueTask ApplyTableStyleAsync(
        RibbonGalleryItem? item,
        IRibbonContextSnapshotProvider snapshotProvider,
        IEditorCommandRouter commandRouter)
    {
        if (item is null)
        {
            return ValueTask.CompletedTask;
        }

        var styleId = item.Id == "none" ? null : item.Id;
        var snapshot = snapshotProvider.GetSnapshot();
        _ = commandRouter.ExecuteAsync(EditorTableCommandIds.Design.ApplyStyle, styleId, snapshot);
        return ValueTask.CompletedTask;
    }

    private IEnumerable<RibbonGroup> BuildLayoutGroups(
        RibbonGroup rowsColumnsGroup,
        RibbonGroup mergeGroup,
        RibbonGroup cellSizeGroup,
        RibbonGroup layoutGroup,
        RibbonGroup alignmentGroup,
        Func<bool> canExecute)
    {
        if (_openPropertiesDialog is null)
        {
            return new[] { rowsColumnsGroup, mergeGroup, cellSizeGroup, layoutGroup, alignmentGroup };
        }

        var propertiesButton = new RibbonButton(
            "table-properties",
            "Properties",
            new RibbonCommand(_openPropertiesDialog, canExecute),
            iconKey: "RibbonIcon.Table",
            size: RibbonControlSize.Small);

        var tableGroup = new RibbonGroup(
            "table-group",
            "Table",
            new IRibbonControl[] { propertiesButton },
            keyTip: "TP");

        return new[] { tableGroup, rowsColumnsGroup, mergeGroup, cellSizeGroup, layoutGroup, alignmentGroup };
    }
}

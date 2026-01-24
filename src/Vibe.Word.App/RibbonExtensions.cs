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

internal sealed class TableRibbonExtension : IRibbonExtension
{
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

        ValueTask ExecuteAsync(string commandId)
        {
            var snapshot = snapshotProvider.GetSnapshot();
            _ = commandRouter.ExecuteAsync(commandId, null, snapshot);
            return ValueTask.CompletedTask;
        }

        RibbonCommand CreateCommand(string commandId)
        {
            return new RibbonCommand(() => ExecuteAsync(commandId), () => CanExecute(commandId));
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

        var layoutGroup = new RibbonGroup(
            "table-layout-group",
            "Layout",
            new IRibbonControl[]
            {
                autoFitButton,
                distributeColumns,
                distributeRows
            },
            keyTip: "LY");

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
            .AddGroups(new[] { rowsColumnsGroup, mergeGroup, layoutGroup, alignmentGroup });
    }
}

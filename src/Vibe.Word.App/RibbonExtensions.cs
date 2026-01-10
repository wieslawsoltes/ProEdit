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

        bool IsActive()
        {
            var snapshot = snapshotProvider.GetSnapshot();
            return snapshot.Selection.IsInTable;
        }

        var contextualSet = new RibbonContextualTabSet(
            "table-tools",
            "Table Tools",
            IsActive,
            accentKey: "Table");
        builder.AddContextualSet(contextualSet);

        var tableGroup = new RibbonGroup(
            "table",
            "Table",
            new IRibbonControl[]
            {
                new RibbonButton(
                    "table-properties",
                    "Properties",
                    isEnabled: false,
                    size: RibbonControlSize.Medium)
            },
            keyTip: "TB");

        builder.AddTab(
                "table-layout",
                "Layout",
                keyTip: "T",
                contextualSet: contextualSet)
            .AddGroup(tableGroup);
    }
}

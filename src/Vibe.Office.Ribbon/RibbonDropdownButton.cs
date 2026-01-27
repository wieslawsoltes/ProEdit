namespace Vibe.Office.Ribbon;

public sealed class RibbonDropdownButton : RibbonControlBase
{
    public RibbonDropdownButton(
        string id,
        string label,
        RibbonMenu menu,
        string? keyTip = null,
        string? iconKey = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? canExecute = null,
        Func<bool>? isVisibleEvaluator = null,
        RibbonControlSize size = RibbonControlSize.Medium,
        string? toolTipDescription = null)
        : base(
            id,
            label,
            keyTip,
            iconKey,
            size,
            isEnabled,
            isVisible,
            canExecute,
            isVisibleEvaluator,
            toolTipDescription)
    {
        Menu = menu ?? throw new ArgumentNullException(nameof(menu));
    }

    public RibbonMenu Menu { get; }

    public override void RefreshState()
    {
        base.RefreshState();
        foreach (var entry in Menu.Items)
        {
            if (entry is IRibbonStateful stateful)
            {
                stateful.RefreshState();
            }
        }
    }
}

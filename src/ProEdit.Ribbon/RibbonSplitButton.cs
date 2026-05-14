namespace ProEdit.Ribbon;

public sealed class RibbonSplitButton : RibbonControlBase
{
    public RibbonSplitButton(
        string id,
        string label,
        IRibbonCommand? primaryCommand,
        RibbonMenu menu,
        string? keyTip = null,
        string? iconKey = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? canExecute = null,
        Func<bool>? isVisibleEvaluator = null,
        RibbonControlSize size = RibbonControlSize.Medium,
        string? toolTipDescription = null,
        string? compactLabel = null,
        RibbonLabelMode labelMode = RibbonLabelMode.Auto)
        : base(
            id,
            label,
            keyTip,
            iconKey,
            size,
            isEnabled,
            isVisible,
            canExecute ?? (primaryCommand is null ? null : () => primaryCommand.CanExecute()),
            isVisibleEvaluator,
            toolTipDescription,
            compactLabel,
            labelMode)
    {
        PrimaryCommand = primaryCommand;
        Menu = menu ?? throw new ArgumentNullException(nameof(menu));
    }

    public IRibbonCommand? PrimaryCommand { get; }
    public RibbonMenu Menu { get; }

    public ValueTask ExecutePrimaryAsync()
    {
        if (!IsEnabled || !IsVisible || PrimaryCommand is null)
        {
            return ValueTask.CompletedTask;
        }

        return PrimaryCommand.ExecuteAsync();
    }

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

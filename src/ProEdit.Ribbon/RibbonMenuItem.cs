namespace ProEdit.Ribbon;

public sealed class RibbonMenuItem : RibbonControlBase, IRibbonMenuEntry
{
    public RibbonMenuItem(
        string id,
        string label,
        IRibbonCommand? command = null,
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
            null,
            iconKey,
            size,
            isEnabled,
            isVisible,
            canExecute ?? (command is null ? null : () => command.CanExecute()),
            isVisibleEvaluator,
            toolTipDescription,
            compactLabel,
            labelMode)
    {
        Command = command;
    }

    public IRibbonCommand? Command { get; }

    public ValueTask ExecuteAsync()
    {
        if (!IsEnabled || !IsVisible || Command is null)
        {
            return ValueTask.CompletedTask;
        }

        return Command.ExecuteAsync();
    }

    public override void RefreshState()
    {
        base.RefreshState();
    }
}

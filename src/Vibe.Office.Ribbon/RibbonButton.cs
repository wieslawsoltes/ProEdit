namespace Vibe.Office.Ribbon;

public sealed class RibbonButton : RibbonControlBase
{
    public RibbonButton(
        string id,
        string label,
        IRibbonCommand? command = null,
        string? keyTip = null,
        string? iconKey = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? canExecute = null,
        Func<bool>? isVisibleEvaluator = null,
        RibbonControlSize size = RibbonControlSize.Medium)
        : base(
            id,
            label,
            keyTip,
            iconKey,
            size,
            isEnabled,
            isVisible,
            canExecute ?? (command is null ? null : () => command.CanExecute()),
            isVisibleEvaluator)
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

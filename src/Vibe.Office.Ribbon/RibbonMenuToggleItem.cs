namespace Vibe.Office.Ribbon;

public sealed class RibbonMenuToggleItem : RibbonControlBase, IRibbonMenuEntry
{
    private bool _isChecked;
    private readonly Func<bool>? _isCheckedEvaluator;

    public RibbonMenuToggleItem(
        string id,
        string label,
        Func<bool>? isCheckedEvaluator = null,
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
        _isCheckedEvaluator = isCheckedEvaluator;
        Command = command;
        _isChecked = _isCheckedEvaluator?.Invoke() ?? false;
    }

    public IRibbonCommand? Command { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value, nameof(IsChecked));
    }

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
        if (_isCheckedEvaluator is not null)
        {
            IsChecked = _isCheckedEvaluator();
        }
    }
}

namespace Vibe.Office.Ribbon;

public sealed class RibbonToggleButton : RibbonControlBase
{
    private bool _isChecked;
    private readonly Func<bool>? _isCheckedEvaluator;
    private readonly Func<bool, ValueTask>? _toggleHandler;

    public RibbonToggleButton(
        string id,
        string label,
        Func<bool>? isCheckedEvaluator = null,
        Func<bool, ValueTask>? toggleHandler = null,
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
        _isCheckedEvaluator = isCheckedEvaluator;
        _toggleHandler = toggleHandler;
        Command = command;
        _isChecked = _isCheckedEvaluator?.Invoke() ?? false;
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value, nameof(IsChecked));
    }

    public IRibbonCommand? Command { get; }

    public async ValueTask ToggleAsync(bool isChecked)
    {
        if (!IsEnabled || !IsVisible)
        {
            return;
        }

        if (_toggleHandler is not null)
        {
            await _toggleHandler(isChecked);
        }

        IsChecked = isChecked;
        if (Command is not null)
        {
            await Command.ExecuteAsync();
        }
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

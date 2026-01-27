namespace Vibe.Office.Ribbon;

public sealed class RibbonGroupLauncher : RibbonStateNode
{
    private readonly IRibbonCommand _command;

    public RibbonGroupLauncher(
        string id,
        string label,
        IRibbonCommand command,
        string? keyTip = null,
        string? iconKey = null,
        string? toolTip = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null)
        : base(isEnabled, isVisible, ResolveIsEnabledEvaluator(command, isEnabledEvaluator), isVisibleEvaluator)
    {
        _command = command ?? throw new ArgumentNullException(nameof(command));
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        KeyTip = keyTip;
        IconKey = iconKey;
        ToolTipDescription = NormalizeToolTipDescription(toolTip);
        ToolTipShortcut = BuildToolTipShortcut(keyTip);
        ToolTip = ToolTipDescription ?? Label;
    }

    public string Id { get; }
    public string Label { get; }
    public string? KeyTip { get; }
    public string? IconKey { get; }
    public string ToolTip { get; }
    public string? ToolTipDescription { get; }
    public string? ToolTipShortcut { get; }
    public object ToolTipContent =>
        string.IsNullOrWhiteSpace(ToolTipDescription) && string.IsNullOrWhiteSpace(ToolTipShortcut)
            ? ToolTip
            : this;
    public IRibbonCommand Command => _command;

    public ValueTask ExecuteAsync()
    {
        if (!IsEnabled)
        {
            return ValueTask.CompletedTask;
        }

        return _command.ExecuteAsync();
    }

    private static Func<bool>? ResolveIsEnabledEvaluator(IRibbonCommand? command, Func<bool>? isEnabledEvaluator)
    {
        if (isEnabledEvaluator is not null)
        {
            return isEnabledEvaluator;
        }

        return command is null ? null : new Func<bool>(command.CanExecute);
    }

    private static string? BuildToolTipShortcut(string? keyTip)
    {
        if (string.IsNullOrWhiteSpace(keyTip))
        {
            return null;
        }

        return $"KeyTip: {keyTip}";
    }

    private static string? NormalizeToolTipDescription(string? toolTipDescription)
    {
        if (string.IsNullOrWhiteSpace(toolTipDescription))
        {
            return null;
        }

        return toolTipDescription;
    }
}

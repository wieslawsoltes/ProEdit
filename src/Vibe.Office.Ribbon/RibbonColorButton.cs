namespace Vibe.Office.Ribbon;

public sealed class RibbonColorButton : RibbonControlBase
{
    private RibbonColorItem? _selectedColor;
    private readonly Func<RibbonColorItem?>? _selectedColorEvaluator;
    private readonly Func<RibbonColorItem?, ValueTask>? _selectionHandler;

    public RibbonColorButton(
        string id,
        string label,
        IReadOnlyList<RibbonColorItem> palette,
        RibbonColorItem? selectedColor = null,
        Func<RibbonColorItem?>? selectedColorEvaluator = null,
        Func<RibbonColorItem?, ValueTask>? selectionHandler = null,
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
        Palette = palette ?? throw new ArgumentNullException(nameof(palette));
        _selectedColorEvaluator = selectedColorEvaluator;
        _selectionHandler = selectionHandler;
        Command = command;
        _selectedColor = _selectedColorEvaluator?.Invoke() ?? selectedColor;
    }

    public IReadOnlyList<RibbonColorItem> Palette { get; }
    public IRibbonCommand? Command { get; }

    public RibbonColorItem? SelectedColor
    {
        get => _selectedColor;
        private set => SetField(ref _selectedColor, value, nameof(SelectedColor));
    }

    public ValueTask ExecuteAsync()
    {
        if (!IsEnabled || !IsVisible || Command is null)
        {
            return ValueTask.CompletedTask;
        }

        return Command.ExecuteAsync();
    }

    public async ValueTask SelectColorAsync(RibbonColorItem? color)
    {
        if (!IsEnabled || !IsVisible)
        {
            return;
        }

        if (color is not null && (!color.IsEnabled || !color.IsVisible))
        {
            return;
        }

        if (_selectionHandler is not null)
        {
            await _selectionHandler(color);
        }

        SelectedColor = color;
    }

    public override void RefreshState()
    {
        base.RefreshState();

        foreach (var color in Palette)
        {
            if (color is IRibbonStateful stateful)
            {
                stateful.RefreshState();
            }
        }

        if (_selectedColorEvaluator is not null)
        {
            SelectedColor = _selectedColorEvaluator();
        }
    }
}

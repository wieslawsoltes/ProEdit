namespace Vibe.Office.Ribbon;

public sealed class RibbonColorSplitButton : RibbonControlBase
{
    private RibbonColorItem? _selectedColor;
    private readonly Func<RibbonColorItem?>? _selectedColorEvaluator;
    private readonly Func<RibbonColorItem?, ValueTask>? _selectionHandler;

    public RibbonColorSplitButton(
        string id,
        string label,
        IRibbonCommand? primaryCommand,
        IReadOnlyList<RibbonColorItem> palette,
        RibbonColorItem? selectedColor = null,
        Func<RibbonColorItem?>? selectedColorEvaluator = null,
        Func<RibbonColorItem?, ValueTask>? selectionHandler = null,
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
            canExecute ?? (primaryCommand is null ? null : () => primaryCommand.CanExecute()),
            isVisibleEvaluator,
            toolTipDescription)
    {
        PrimaryCommand = primaryCommand;
        Palette = palette ?? throw new ArgumentNullException(nameof(palette));
        _selectedColorEvaluator = selectedColorEvaluator;
        _selectionHandler = selectionHandler;
        _selectedColor = _selectedColorEvaluator?.Invoke() ?? selectedColor;
    }

    public IRibbonCommand? PrimaryCommand { get; }
    public IReadOnlyList<RibbonColorItem> Palette { get; }

    public RibbonColorItem? SelectedColor
    {
        get => _selectedColor;
        private set => SetField(ref _selectedColor, value, nameof(SelectedColor));
    }

    public ValueTask ExecutePrimaryAsync()
    {
        if (!IsEnabled || !IsVisible || PrimaryCommand is null)
        {
            return ValueTask.CompletedTask;
        }

        return PrimaryCommand.ExecuteAsync();
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

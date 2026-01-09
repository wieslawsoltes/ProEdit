namespace Vibe.Office.Ribbon;

public sealed class RibbonGallery : RibbonControlBase
{
    private RibbonGalleryItem? _selectedItem;
    private readonly Func<RibbonGalleryItem?>? _selectedItemEvaluator;
    private readonly Func<RibbonGalleryItem?, ValueTask>? _selectionHandler;

    public RibbonGallery(
        string id,
        string label,
        IReadOnlyList<RibbonGalleryItem> items,
        RibbonGalleryItem? selectedItem = null,
        Func<RibbonGalleryItem?>? selectedItemEvaluator = null,
        Func<RibbonGalleryItem?, ValueTask>? selectionHandler = null,
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
            canExecute,
            isVisibleEvaluator)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        _selectedItemEvaluator = selectedItemEvaluator;
        _selectionHandler = selectionHandler;
        _selectedItem = _selectedItemEvaluator?.Invoke() ?? selectedItem;
    }

    public IReadOnlyList<RibbonGalleryItem> Items { get; }

    public RibbonGalleryItem? SelectedItem
    {
        get => _selectedItem;
        private set => SetField(ref _selectedItem, value, nameof(SelectedItem));
    }

    public async ValueTask SelectAsync(RibbonGalleryItem? item)
    {
        if (!IsEnabled || !IsVisible)
        {
            return;
        }

        if (item is not null && (!item.IsEnabled || !item.IsVisible))
        {
            return;
        }

        if (_selectionHandler is not null)
        {
            await _selectionHandler(item);
        }

        SelectedItem = item;
    }

    public override void RefreshState()
    {
        base.RefreshState();

        foreach (var item in Items)
        {
            if (item is IRibbonStateful stateful)
            {
                stateful.RefreshState();
            }
        }

        if (_selectedItemEvaluator is not null)
        {
            SelectedItem = _selectedItemEvaluator();
        }
    }
}

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
        bool showDropDown = false,
        string? keyTip = null,
        string? iconKey = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? canExecute = null,
        Func<bool>? isVisibleEvaluator = null,
        RibbonControlSize size = RibbonControlSize.Medium,
        int popupColumns = 1,
        double popupMinWidth = 240,
        double popupMaxHeight = 320,
        double popupItemMinWidth = 200,
        RibbonMenu? popupMenu = null,
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
        Items = items ?? throw new ArgumentNullException(nameof(items));
        _selectedItemEvaluator = selectedItemEvaluator;
        _selectionHandler = selectionHandler;
        _selectedItem = _selectedItemEvaluator?.Invoke() ?? selectedItem;
        ShowDropDown = showDropDown;
        PopupColumns = Math.Max(1, popupColumns);
        PopupMinWidth = Math.Max(0, popupMinWidth);
        PopupMaxHeight = Math.Max(0, popupMaxHeight);
        PopupItemMinWidth = Math.Max(0, popupItemMinWidth);
        PopupMenu = popupMenu;
    }

    public IReadOnlyList<RibbonGalleryItem> Items { get; }
    public bool ShowDropDown { get; }
    public int PopupColumns { get; }
    public double PopupMinWidth { get; }
    public double PopupMaxHeight { get; }
    public double PopupItemMinWidth { get; }
    public RibbonMenu? PopupMenu { get; }
    public bool HasPopupMenu => PopupMenu is not null && PopupMenu.Items.Count > 0;

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

        if (item is null)
        {
            if (_selectedItem is null)
            {
                return;
            }

            SelectedItem = null;
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

        if (PopupMenu is not null)
        {
            foreach (var entry in PopupMenu.Items)
            {
                if (entry is IRibbonStateful stateful)
                {
                    stateful.RefreshState();
                }
            }
        }

        if (_selectedItemEvaluator is not null)
        {
            SelectedItem = _selectedItemEvaluator();
        }
    }
}

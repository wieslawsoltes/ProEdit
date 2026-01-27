namespace Vibe.Office.Ribbon;

public sealed class RibbonComboBox : RibbonControlBase
{
    private string? _text;
    private RibbonComboBoxItem? _selectedItem;
    private readonly Func<string?>? _textEvaluator;
    private readonly Func<RibbonComboBoxItem?>? _selectedItemEvaluator;
    private readonly Func<string?, ValueTask>? _textChangedHandler;
    private readonly Func<RibbonComboBoxItem?, ValueTask>? _selectionHandler;

    public RibbonComboBox(
        string id,
        string label,
        IReadOnlyList<RibbonComboBoxItem> items,
        bool isEditable = true,
        string? text = null,
        RibbonComboBoxItem? selectedItem = null,
        Func<string?>? textEvaluator = null,
        Func<RibbonComboBoxItem?>? selectedItemEvaluator = null,
        Func<string?, ValueTask>? textChangedHandler = null,
        Func<RibbonComboBoxItem?, ValueTask>? selectionHandler = null,
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
            canExecute,
            isVisibleEvaluator,
            toolTipDescription)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        IsEditable = isEditable;
        _textEvaluator = textEvaluator;
        _selectedItemEvaluator = selectedItemEvaluator;
        _textChangedHandler = textChangedHandler;
        _selectionHandler = selectionHandler;

        if (_selectedItemEvaluator is not null)
        {
            ApplySelection(_selectedItemEvaluator(), updateText: true);
        }
        else if (_textEvaluator is not null)
        {
            UpdateTextAndSelection(_textEvaluator());
        }
        else
        {
            _selectedItem = selectedItem;
            _text = text ?? GetItemText(selectedItem);
        }
    }

    public IReadOnlyList<RibbonComboBoxItem> Items { get; }
    public bool IsEditable { get; }

    public string? Text
    {
        get => _text;
        private set => SetField(ref _text, value, nameof(Text));
    }

    public RibbonComboBoxItem? SelectedItem
    {
        get => _selectedItem;
        private set => SetField(ref _selectedItem, value, nameof(SelectedItem));
    }

    public async ValueTask SelectAsync(RibbonComboBoxItem? item)
    {
        if (!IsEnabled || !IsVisible)
        {
            return;
        }

        if (item is not null && (!item.IsEnabled || !item.IsVisible))
        {
            return;
        }

        if (IsSameItem(item, _selectedItem))
        {
            ApplySelection(item, updateText: true);
            return;
        }

        if (_selectionHandler is not null)
        {
            await _selectionHandler(item);
        }

        ApplySelection(item, updateText: true);
    }

    public async ValueTask UpdateTextAsync(string? text)
    {
        if (!IsEnabled || !IsVisible || !IsEditable)
        {
            return;
        }

        if (string.Equals(text, Text, StringComparison.Ordinal))
        {
            return;
        }

        if (_textChangedHandler is not null)
        {
            await _textChangedHandler(text);
        }

        UpdateTextAndSelection(text);
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
            ApplySelection(_selectedItemEvaluator(), updateText: true);
            return;
        }

        if (_textEvaluator is not null)
        {
            UpdateTextAndSelection(_textEvaluator());
        }
    }

    private void ApplySelection(RibbonComboBoxItem? item, bool updateText)
    {
        SelectedItem = item;
        if (updateText)
        {
            Text = GetItemText(item);
        }
    }

    private void UpdateTextAndSelection(string? text)
    {
        Text = text;
        SelectedItem = ResolveItem(text);
    }

    private RibbonComboBoxItem? ResolveItem(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var item in Items)
        {
            if (string.Equals(item.Value, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Label, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Id, text, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static string? GetItemText(RibbonComboBoxItem? item)
    {
        if (item is null)
        {
            return null;
        }

        return item.Value ?? item.Label;
    }

    private static bool IsSameItem(RibbonComboBoxItem? left, RibbonComboBoxItem? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
    }
}

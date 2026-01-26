using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Vibe.Office.Ribbon;

public sealed class RibbonModel : INotifyPropertyChanged
{
    private RibbonTab? _selectedTab;
    private RibbonTab? _lastNonContextualTab;
    private HashSet<string> _activeContextualSetIds = new(StringComparer.OrdinalIgnoreCase);

    public RibbonModel(
        IEnumerable<RibbonTab> tabs,
        IEnumerable<RibbonQuickAccessItem>? quickAccess = null,
        IEnumerable<RibbonContextualTabSet>? contextualSets = null)
    {
        Tabs = new ObservableCollection<RibbonTab>(tabs ?? throw new ArgumentNullException(nameof(tabs)));
        QuickAccess = new ObservableCollection<RibbonQuickAccessItem>(
            quickAccess ?? Array.Empty<RibbonQuickAccessItem>());
        ContextualSets = new ObservableCollection<RibbonContextualTabSet>(
            contextualSets ?? Array.Empty<RibbonContextualTabSet>());
        SelectedTab = Tabs.Count > 0 ? Tabs[0] : null;
    }

    public ObservableCollection<RibbonTab> Tabs { get; }
    public ObservableCollection<RibbonQuickAccessItem> QuickAccess { get; }
    public ObservableCollection<RibbonContextualTabSet> ContextualSets { get; }

    public RibbonTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!ReferenceEquals(_selectedTab, value))
            {
                _selectedTab = value;
                if (value is { ContextualSet: null })
                {
                    _lastNonContextualTab = value;
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTab)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddQuickAccess(IRibbonControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (ContainsQuickAccess(control.Id))
        {
            return;
        }

        QuickAccess.Add(new RibbonQuickAccessItem(control));
    }

    public bool RemoveQuickAccess(string controlId)
    {
        for (var i = 0; i < QuickAccess.Count; i++)
        {
            if (string.Equals(QuickAccess[i].Control.Id, controlId, StringComparison.OrdinalIgnoreCase))
            {
                QuickAccess.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public bool ContainsQuickAccess(string controlId)
    {
        foreach (var item in QuickAccess)
        {
            if (string.Equals(item.Control.Id, controlId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void RefreshState()
    {
        RibbonContextualTabSet? newlyActiveSet = null;
        var nextActiveSetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contextualSet in ContextualSets)
        {
            contextualSet.RefreshState();
            if (contextualSet.IsActive)
            {
                nextActiveSetIds.Add(contextualSet.Id);
                if (newlyActiveSet is null && !_activeContextualSetIds.Contains(contextualSet.Id))
                {
                    newlyActiveSet = contextualSet;
                }
            }
        }

        foreach (var tab in Tabs)
        {
            tab.RefreshState();
        }

        foreach (var item in QuickAccess)
        {
            if (item.Control is IRibbonStateful stateful)
            {
                stateful.RefreshState();
            }
        }

        if (newlyActiveSet is not null)
        {
            var contextualTab = Tabs.FirstOrDefault(tab =>
                tab.IsVisible && tab.ContextualSetId == newlyActiveSet.Id);
            if (contextualTab is not null)
            {
                SelectedTab = contextualTab;
            }
        }

        if (SelectedTab is { ContextualSet: not null } selectedTab
            && selectedTab.ContextualSet is { } selectedSet
            && !selectedSet.IsActive)
        {
            if (_lastNonContextualTab is not null && _lastNonContextualTab.IsVisible)
            {
                SelectedTab = _lastNonContextualTab;
            }
        }

        if (SelectedTab is null || !SelectedTab.IsVisible)
        {
            SelectedTab = Tabs.FirstOrDefault(tab => tab.IsVisible) ?? Tabs.FirstOrDefault();
        }

        _activeContextualSetIds = nextActiveSetIds;
    }
}

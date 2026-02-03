using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Vibe.Office.Ribbon;

public sealed class RibbonModel : INotifyPropertyChanged
{
    private RibbonTab? _selectedTab;
    private RibbonTab? _lastNonContextualTab;
    private HashSet<string> _activeContextualSetIds = new(StringComparer.OrdinalIgnoreCase);
    private IRibbonControl? _topBarSearch;
    private string? _topBarAppName;
    private string? _topBarAppBadge;
    private string? _topBarTitle;
    private string? _topBarStatusText;
    private string? _topBarStatusIconKey;
    private string? _topBarProfileInitials;

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

    public IRibbonControl? TopBarSearch
    {
        get => _topBarSearch;
        set
        {
            if (!ReferenceEquals(_topBarSearch, value))
            {
                _topBarSearch = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopBarSearch)));
            }
        }
    }

    public string? TopBarAppName
    {
        get => _topBarAppName;
        set
        {
            if (!string.Equals(_topBarAppName, value, StringComparison.Ordinal))
            {
                _topBarAppName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopBarAppName)));
            }
        }
    }

    public string? TopBarAppBadge
    {
        get => _topBarAppBadge;
        set
        {
            if (!string.Equals(_topBarAppBadge, value, StringComparison.Ordinal))
            {
                _topBarAppBadge = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopBarAppBadge)));
            }
        }
    }

    public string? TopBarTitle
    {
        get => _topBarTitle;
        set
        {
            if (!string.Equals(_topBarTitle, value, StringComparison.Ordinal))
            {
                _topBarTitle = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopBarTitle)));
            }
        }
    }

    public string? TopBarStatusText
    {
        get => _topBarStatusText;
        set
        {
            if (!string.Equals(_topBarStatusText, value, StringComparison.Ordinal))
            {
                _topBarStatusText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopBarStatusText)));
            }
        }
    }

    public string? TopBarStatusIconKey
    {
        get => _topBarStatusIconKey;
        set
        {
            if (!string.Equals(_topBarStatusIconKey, value, StringComparison.Ordinal))
            {
                _topBarStatusIconKey = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopBarStatusIconKey)));
            }
        }
    }

    public string? TopBarProfileInitials
    {
        get => _topBarProfileInitials;
        set
        {
            if (!string.Equals(_topBarProfileInitials, value, StringComparison.Ordinal))
            {
                _topBarProfileInitials = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopBarProfileInitials)));
            }
        }
    }

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

        if (TopBarSearch is IRibbonStateful topBarStateful)
        {
            topBarStateful.RefreshState();
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

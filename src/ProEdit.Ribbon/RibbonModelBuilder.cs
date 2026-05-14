namespace ProEdit.Ribbon;

public sealed class RibbonModelBuilder
{
    private readonly List<RibbonTabBuilder> _tabs = new();
    private readonly Dictionary<string, RibbonTabBuilder> _tabMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RibbonQuickAccessItem> _quickAccess = new();
    private readonly HashSet<string> _quickAccessIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RibbonContextualTabSet> _contextualSets = new();
    private readonly HashSet<string> _contextualSetIds = new(StringComparer.OrdinalIgnoreCase);
    private IRibbonControl? _topBarSearch;
    private string? _topBarAppName;
    private string? _topBarAppBadge;
    private string? _topBarTitle;
    private string? _topBarStatusText;
    private string? _topBarStatusIconKey;
    private string? _topBarProfileInitials;

    public RibbonTabBuilder AddTab(
        string id,
        string header,
        string? keyTip = null,
        RibbonContextualTabSet? contextualSet = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null)
    {
        if (_tabMap.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var builder = new RibbonTabBuilder(
            id,
            header,
            keyTip,
            contextualSet,
            isEnabled,
            isVisible,
            isEnabledEvaluator,
            isVisibleEvaluator);
        _tabMap[id] = builder;
        _tabs.Add(builder);
        if (contextualSet is not null)
        {
            AddContextualSet(contextualSet);
        }

        return builder;
    }

    public bool TryGetTab(string id, out RibbonTabBuilder builder)
    {
        return _tabMap.TryGetValue(id, out builder!);
    }

    public void AddQuickAccess(IRibbonControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (!_quickAccessIds.Add(control.Id))
        {
            return;
        }

        _quickAccess.Add(new RibbonQuickAccessItem(control));
    }

    public void AddContextualSet(RibbonContextualTabSet contextualSet)
    {
        ArgumentNullException.ThrowIfNull(contextualSet);
        if (!_contextualSetIds.Add(contextualSet.Id))
        {
            return;
        }

        _contextualSets.Add(contextualSet);
    }

    public void SetTopBarSearch(IRibbonControl? control)
    {
        _topBarSearch = control;
    }

    public void SetTopBarAppName(string? name)
    {
        _topBarAppName = name;
    }

    public void SetTopBarAppBadge(string? badge)
    {
        _topBarAppBadge = badge;
    }

    public void SetTopBarTitle(string? title)
    {
        _topBarTitle = title;
    }

    public void SetTopBarStatus(string? statusText, string? statusIconKey = null)
    {
        _topBarStatusText = statusText;
        _topBarStatusIconKey = statusIconKey;
    }

    public void SetTopBarProfileInitials(string? initials)
    {
        _topBarProfileInitials = initials;
    }

    public void ApplyExtensions(IEnumerable<IRibbonExtension> extensions, RibbonExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var extension in extensions)
        {
            extension.Build(this, context);
        }
    }

    public RibbonModel Build()
    {
        var tabs = new List<RibbonTab>(_tabs.Count);
        var contextualSets = new List<RibbonContextualTabSet>(_contextualSets);
        var contextualIds = new HashSet<string>(_contextualSetIds, StringComparer.OrdinalIgnoreCase);

        foreach (var builder in _tabs)
        {
            var tab = builder.Build();
            tabs.Add(tab);
            if (tab.ContextualSet is { } set && contextualIds.Add(set.Id))
            {
                contextualSets.Add(set);
            }
        }

        var model = new RibbonModel(tabs, _quickAccess, contextualSets)
        {
            TopBarSearch = _topBarSearch,
            TopBarAppName = _topBarAppName,
            TopBarAppBadge = _topBarAppBadge,
            TopBarTitle = _topBarTitle,
            TopBarStatusText = _topBarStatusText,
            TopBarStatusIconKey = _topBarStatusIconKey,
            TopBarProfileInitials = _topBarProfileInitials
        };
        return model;
    }
}

public sealed class RibbonTabBuilder
{
    private readonly List<RibbonGroup> _groups = new();

    public RibbonTabBuilder(
        string id,
        string header,
        string? keyTip = null,
        RibbonContextualTabSet? contextualSet = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        KeyTip = keyTip;
        ContextualSet = contextualSet;
        IsEnabled = isEnabled;
        IsVisible = isVisible;
        IsEnabledEvaluator = isEnabledEvaluator;
        IsVisibleEvaluator = isVisibleEvaluator;
    }

    public string Id { get; }
    public string Header { get; }
    public string? KeyTip { get; }
    public RibbonContextualTabSet? ContextualSet { get; }
    public bool IsEnabled { get; }
    public bool IsVisible { get; }
    public Func<bool>? IsEnabledEvaluator { get; }
    public Func<bool>? IsVisibleEvaluator { get; }

    public IReadOnlyList<RibbonGroup> Groups => _groups;

    public RibbonTabBuilder AddGroup(RibbonGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        _groups.Add(group);
        return this;
    }

    public RibbonTabBuilder InsertGroup(int index, RibbonGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (index < 0 || index > _groups.Count)
        {
            index = _groups.Count;
        }

        _groups.Insert(index, group);
        return this;
    }

    public RibbonTabBuilder AddGroups(IEnumerable<RibbonGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        foreach (var group in groups)
        {
            AddGroup(group);
        }

        return this;
    }

    internal RibbonTab Build()
    {
        return new RibbonTab(
            Id,
            Header,
            _groups.ToArray(),
            contextualSet: ContextualSet,
            keyTip: KeyTip,
            isEnabled: IsEnabled,
            isVisible: IsVisible,
            isEnabledEvaluator: IsEnabledEvaluator,
            isVisibleEvaluator: IsVisibleEvaluator);
    }
}

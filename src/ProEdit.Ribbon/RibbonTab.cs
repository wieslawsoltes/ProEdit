namespace ProEdit.Ribbon;

public sealed class RibbonTab : RibbonStateNode
{
    public RibbonTab(
        string id,
        string header,
        IReadOnlyList<RibbonGroup> groups,
        RibbonContextualTabSet? contextualSet = null,
        string? keyTip = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null)
        : base(isEnabled, isVisible, isEnabledEvaluator, isVisibleEvaluator)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        ContextualSet = contextualSet;
        KeyTip = keyTip;
    }

    public string Id { get; }
    public string Header { get; }
    public IReadOnlyList<RibbonGroup> Groups { get; }
    public RibbonContextualTabSet? ContextualSet { get; }
    public string? ContextualSetId => ContextualSet?.Id;
    public string? ContextualHeader => ContextualSet?.Header;
    public bool HasContextualSet => ContextualSet is not null;
    public string? KeyTip { get; }

    public override void RefreshState()
    {
        var baseEnabled = EvaluateIsEnabled();
        var baseVisible = EvaluateIsVisible();
        IsEnabled = baseEnabled;
        IsVisible = ContextualSet is null ? baseVisible : baseVisible && ContextualSet.IsActive;
        foreach (var group in Groups)
        {
            group.RefreshState();
        }
    }
}

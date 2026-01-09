namespace Vibe.Office.Ribbon;

public sealed class RibbonContextualTabSet : RibbonStateNode
{
    private bool _isActive;
    private readonly Func<bool>? _isActiveEvaluator;

    public RibbonContextualTabSet(
        string id,
        string header,
        Func<bool>? isActiveEvaluator = null,
        string? accentKey = null,
        bool isVisible = true)
        : base(isEnabled: true, isVisible: isVisible)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        AccentKey = accentKey;
        _isActiveEvaluator = isActiveEvaluator;
        _isActive = _isActiveEvaluator?.Invoke() ?? isVisible;
    }

    public string Id { get; }
    public string Header { get; }
    public string? AccentKey { get; }

    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value, nameof(IsActive));
    }

    public override void RefreshState()
    {
        base.RefreshState();
        IsActive = _isActiveEvaluator?.Invoke() ?? IsVisible;
    }
}

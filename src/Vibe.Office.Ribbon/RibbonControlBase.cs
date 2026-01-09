namespace Vibe.Office.Ribbon;

public abstract class RibbonControlBase : RibbonStateNode, IRibbonControl
{
    private RibbonControlSize _layoutSize;

    protected RibbonControlBase(
        string id,
        string label,
        string? keyTip = null,
        string? iconKey = null,
        RibbonControlSize size = RibbonControlSize.Medium,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null)
        : base(isEnabled, isVisible, isEnabledEvaluator, isVisibleEvaluator)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        KeyTip = keyTip;
        IconKey = iconKey;
        Size = size;
        _layoutSize = size;
    }

    public string Id { get; }
    public string Label { get; }
    public string? KeyTip { get; }
    public string? IconKey { get; }
    public RibbonControlSize Size { get; }
    public RibbonControlSize LayoutSize
    {
        get => _layoutSize;
        private set => SetField(ref _layoutSize, value, nameof(LayoutSize));
    }

    internal void SetLayoutSize(RibbonControlSize size)
    {
        LayoutSize = size;
    }
}

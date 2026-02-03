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
        Func<bool>? isVisibleEvaluator = null,
        string? toolTipDescription = null,
        string? compactLabel = null,
        RibbonLabelMode labelMode = RibbonLabelMode.Auto)
        : base(isEnabled, isVisible, isEnabledEvaluator, isVisibleEvaluator)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        KeyTip = keyTip;
        IconKey = iconKey;
        Size = size;
        _layoutSize = size;
        CompactLabel = string.IsNullOrWhiteSpace(compactLabel) ? null : compactLabel;
        LabelMode = labelMode;
        ToolTipDescription = NormalizeToolTipDescription(toolTipDescription);
        ToolTipShortcut = BuildToolTipShortcut(keyTip);
        ToolTip = ToolTipDescription ?? Label;
    }

    public string Id { get; }
    public string Label { get; }
    public string? KeyTip { get; }
    public string? IconKey { get; }
    public RibbonControlSize Size { get; }
    public string? CompactLabel { get; }
    public RibbonLabelMode LabelMode { get; }
    public string ToolTip { get; }
    public string? ToolTipDescription { get; }
    public string? ToolTipShortcut { get; }
    public object ToolTipContent =>
        string.IsNullOrWhiteSpace(ToolTipDescription) && string.IsNullOrWhiteSpace(ToolTipShortcut)
            ? ToolTip
            : this;
    public RibbonControlSize LayoutSize
    {
        get => _layoutSize;
        private set => SetField(ref _layoutSize, value, nameof(LayoutSize));
    }

    internal void SetLayoutSize(RibbonControlSize size)
    {
        LayoutSize = size;
    }

    private static string? BuildToolTipShortcut(string? keyTip)
    {
        if (string.IsNullOrWhiteSpace(keyTip))
        {
            return null;
        }

        return $"KeyTip: {keyTip}";
    }

    private static string? NormalizeToolTipDescription(string? toolTipDescription)
    {
        if (string.IsNullOrWhiteSpace(toolTipDescription))
        {
            return null;
        }

        return toolTipDescription;
    }
}

namespace Vibe.Office.Ribbon;

public sealed class RibbonGroup : RibbonStateNode
{
    public RibbonGroup(
        string id,
        string header,
        IReadOnlyList<IRibbonControl> controls,
        RibbonGroupSizeMode sizeMode = RibbonGroupSizeMode.Large,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null)
        : base(isEnabled, isVisible, isEnabledEvaluator, isVisibleEvaluator)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Controls = controls ?? throw new ArgumentNullException(nameof(controls));
        SizeMode = sizeMode;
    }

    public string Id { get; }
    public string Header { get; }
    public IReadOnlyList<IRibbonControl> Controls { get; }
    public RibbonGroupSizeMode SizeMode { get; }

    public override void RefreshState()
    {
        base.RefreshState();
        foreach (var control in Controls)
        {
            if (control is IRibbonStateful stateful)
            {
                stateful.RefreshState();
            }
        }
    }
}

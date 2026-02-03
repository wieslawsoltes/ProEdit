namespace Vibe.Office.Ribbon;

public sealed class RibbonSeparator : RibbonControlBase
{
    public RibbonSeparator(
        string id,
        RibbonControlSize size = RibbonControlSize.Small,
        bool isVisible = true,
        Func<bool>? isVisibleEvaluator = null,
        RibbonLabelMode labelMode = RibbonLabelMode.ForceHidden)
        : base(
            id,
            string.Empty,
            keyTip: null,
            iconKey: null,
            size: size,
            isEnabled: false,
            isVisible: isVisible,
            isEnabledEvaluator: null,
            isVisibleEvaluator: isVisibleEvaluator,
            labelMode: labelMode)
    {
    }
}

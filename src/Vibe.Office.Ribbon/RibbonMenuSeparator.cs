namespace Vibe.Office.Ribbon;

public sealed class RibbonMenuSeparator : RibbonStateNode, IRibbonMenuEntry
{
    public RibbonMenuSeparator(
        bool isVisible = true,
        Func<bool>? isVisibleEvaluator = null)
        : base(isEnabled: false, isVisible: isVisible, isVisibleEvaluator: isVisibleEvaluator)
    {
    }

    public override void RefreshState()
    {
        base.RefreshState();
    }
}

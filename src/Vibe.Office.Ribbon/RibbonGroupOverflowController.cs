namespace Vibe.Office.Ribbon;

public sealed class RibbonGroupOverflowController
{
    public RibbonGroupOverflowController(double collapseThreshold = 6d, double expandThreshold = 32d)
    {
        CollapseThreshold = collapseThreshold;
        ExpandThreshold = expandThreshold;
    }

    public double CollapseThreshold { get; set; }
    public double ExpandThreshold { get; set; }

    public bool UpdateLayout(IReadOnlyList<RibbonGroup> groups, double availableWidth, double contentWidth)
    {
        if (groups.Count == 0)
        {
            return false;
        }

        if (availableWidth <= 0 || double.IsInfinity(availableWidth))
        {
            return false;
        }

        var collapseThreshold = Math.Max(0, CollapseThreshold);
        var expandThreshold = Math.Max(0, ExpandThreshold);

        if (contentWidth > availableWidth + collapseThreshold)
        {
            if (ShrinkGroups(groups))
            {
                return true;
            }

            return CollapseGroups(groups);
        }

        if (contentWidth < availableWidth - expandThreshold)
        {
            if (ExpandCollapsedGroups(groups))
            {
                return true;
            }

            return ExpandGroups(groups);
        }

        return false;
    }

    private static bool ShrinkGroups(IReadOnlyList<RibbonGroup> groups)
    {
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i].TryStepLayoutMode(shrink: true))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpandGroups(IReadOnlyList<RibbonGroup> groups)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].TryStepLayoutMode(shrink: false))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CollapseGroups(IReadOnlyList<RibbonGroup> groups)
    {
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i].TrySetCollapsed(true))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpandCollapsedGroups(IReadOnlyList<RibbonGroup> groups)
    {
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i].TrySetCollapsed(false))
            {
                return true;
            }
        }

        return false;
    }
}

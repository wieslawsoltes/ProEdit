using System;
using System.Collections.Generic;

namespace ProEdit.Ribbon;

public sealed class RibbonToggleGroup : RibbonControlBase
{
    public RibbonToggleGroup(
        string id,
        string label,
        IReadOnlyList<RibbonToggleButton> items,
        int columns = 1,
        string? keyTip = null,
        string? iconKey = null,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? canExecute = null,
        Func<bool>? isVisibleEvaluator = null,
        RibbonControlSize size = RibbonControlSize.Small,
        string? toolTipDescription = null,
        string? compactLabel = null,
        RibbonLabelMode labelMode = RibbonLabelMode.Auto)
        : base(
            id,
            label,
            keyTip,
            iconKey,
            size,
            isEnabled,
            isVisible,
            canExecute,
            isVisibleEvaluator,
            toolTipDescription,
            compactLabel,
            labelMode)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        Columns = Math.Max(1, columns);
    }

    public IReadOnlyList<RibbonToggleButton> Items { get; }
    public int Columns { get; }

    public override void RefreshState()
    {
        base.RefreshState();

        foreach (var item in Items)
        {
            if (item is IRibbonStateful stateful)
            {
                stateful.RefreshState();
            }
        }
    }
}

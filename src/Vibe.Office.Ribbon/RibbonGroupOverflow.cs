using System;

namespace Vibe.Office.Ribbon;

public sealed class RibbonGroupOverflow : RibbonControlBase
{
    public RibbonGroupOverflow(RibbonGroup group)
        : this(Validate(group), skipValidation: true)
    {
    }

    private RibbonGroupOverflow(RibbonGroup group, bool skipValidation)
        : base(
            id: $"{group.Id}.overflow",
            label: group.Header,
            keyTip: group.KeyTip,
            iconKey: null,
            size: RibbonControlSize.Small,
            isEnabled: group.IsEnabled,
            isVisible: group.IsVisible,
            isEnabledEvaluator: () => group.IsEnabled,
            isVisibleEvaluator: () => group.IsVisible)
    {
        Group = group;
    }

    public RibbonGroup Group { get; }

    public IReadOnlyList<IRibbonControl> Controls => Group.Controls;

    private static RibbonGroup Validate(RibbonGroup group)
    {
        return group ?? throw new ArgumentNullException(nameof(group));
    }
}

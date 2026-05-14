namespace ProEdit.Ribbon;

public sealed class RibbonGroup : RibbonStateNode
{
    private RibbonGroupSizeMode _sizeMode;
    private bool _isCollapsed;

    public RibbonGroup(
        string id,
        string header,
        IReadOnlyList<IRibbonControl> controls,
        RibbonGroupSizeMode sizeMode = RibbonGroupSizeMode.Large,
        bool isEnabled = true,
        bool isVisible = true,
        Func<bool>? isEnabledEvaluator = null,
        Func<bool>? isVisibleEvaluator = null,
        string? keyTip = null,
        RibbonGroupLauncher? launcher = null)
        : base(isEnabled, isVisible, isEnabledEvaluator, isVisibleEvaluator)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Controls = controls ?? throw new ArgumentNullException(nameof(controls));
        KeyTip = keyTip;
        Launcher = launcher;
        PreferredSizeMode = sizeMode;
        _sizeMode = sizeMode;
        Overflow = new RibbonGroupOverflow(this);
        UpdateControlLayoutSizes();
    }

    public string Id { get; }
    public string Header { get; }
    public string? KeyTip { get; }
    public IReadOnlyList<IRibbonControl> Controls { get; }
    public RibbonGroupOverflow Overflow { get; }
    public RibbonGroupLauncher? Launcher { get; }
    public bool HasLauncher => Launcher is not null;
    public RibbonGroupSizeMode PreferredSizeMode { get; }
    public RibbonGroupSizeMode SizeMode
    {
        get => _sizeMode;
        private set => SetField(ref _sizeMode, value, nameof(SizeMode));
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        private set => SetField(ref _isCollapsed, value, nameof(IsCollapsed));
    }

    public void ResetLayoutMode()
    {
        IsCollapsed = false;
        SetLayoutMode(PreferredSizeMode);
    }

    public bool TrySetCollapsed(bool collapsed)
    {
        if (IsCollapsed == collapsed)
        {
            return false;
        }

        IsCollapsed = collapsed;
        return true;
    }

    public bool TryStepLayoutMode(bool shrink)
    {
        var current = SizeMode;
        var next = shrink ? StepDown(current) : StepUp(current);
        if (next == current)
        {
            return false;
        }

        if (!shrink && GetModeRank(next) > GetModeRank(PreferredSizeMode))
        {
            return false;
        }

        SetLayoutMode(next);
        return true;
    }

    private void SetLayoutMode(RibbonGroupSizeMode mode)
    {
        if (SizeMode == mode)
        {
            return;
        }

        SizeMode = mode;
        UpdateControlLayoutSizes();
    }

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

        Overflow.RefreshState();
        Launcher?.RefreshState();
    }

    private void UpdateControlLayoutSizes()
    {
        foreach (var control in Controls)
        {
            if (control is RibbonControlBase baseControl)
            {
                baseControl.SetLayoutSize(ResolveLayoutSize(baseControl.Size, SizeMode));
            }
        }
    }

    private static RibbonControlSize ResolveLayoutSize(RibbonControlSize size, RibbonGroupSizeMode mode)
    {
        return mode switch
        {
            RibbonGroupSizeMode.Large => size,
            RibbonGroupSizeMode.Medium => size switch
            {
                RibbonControlSize.Large => RibbonControlSize.Medium,
                RibbonControlSize.Medium => RibbonControlSize.Small,
                _ => RibbonControlSize.Small
            },
            RibbonGroupSizeMode.Small => RibbonControlSize.Small,
            _ => size
        };
    }

    private static RibbonGroupSizeMode StepDown(RibbonGroupSizeMode mode)
    {
        return mode switch
        {
            RibbonGroupSizeMode.Large => RibbonGroupSizeMode.Medium,
            RibbonGroupSizeMode.Medium => RibbonGroupSizeMode.Small,
            _ => RibbonGroupSizeMode.Small
        };
    }

    private static RibbonGroupSizeMode StepUp(RibbonGroupSizeMode mode)
    {
        return mode switch
        {
            RibbonGroupSizeMode.Small => RibbonGroupSizeMode.Medium,
            RibbonGroupSizeMode.Medium => RibbonGroupSizeMode.Large,
            _ => RibbonGroupSizeMode.Large
        };
    }

    private static int GetModeRank(RibbonGroupSizeMode mode)
    {
        return mode switch
        {
            RibbonGroupSizeMode.Small => 0,
            RibbonGroupSizeMode.Medium => 1,
            RibbonGroupSizeMode.Large => 2,
            _ => 1
        };
    }
}

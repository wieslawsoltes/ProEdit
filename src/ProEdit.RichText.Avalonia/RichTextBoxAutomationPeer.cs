using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace ProEdit.RichText.Avalonia;

internal sealed class RichTextBoxAutomationPeer : ControlAutomationPeer, IValueProvider, IScrollProvider
{
    private const double PercentMin = 0d;
    private const double PercentMax = 100d;
    private readonly RichTextBox _owner;

    public RichTextBoxAutomationPeer(RichTextBox owner)
        : base(owner)
    {
        _owner = owner;
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Document;
    }

    protected override string GetClassNameCore()
    {
        return nameof(RichTextBox);
    }

    protected override object? GetProviderCore(Type providerType)
    {
        if (providerType == typeof(IValueProvider)
            || providerType == typeof(IScrollProvider))
        {
            return this;
        }

        return base.GetProviderCore(providerType);
    }

    bool IValueProvider.IsReadOnly => true;

    string IValueProvider.Value => _owner.GetAutomationTextValueForTests();

    void IValueProvider.SetValue(string? value)
    {
        throw new InvalidOperationException("RichTextBox automation value is read-only.");
    }

    bool IScrollProvider.HorizontallyScrollable => MaxHorizontalOffset > 0d;

    double IScrollProvider.HorizontalScrollPercent =>
        MaxHorizontalOffset <= 0d
            ? ScrollPatternIdentifiers.NoScroll
            : Math.Clamp((_owner.AutomationOffsetForTests.X / MaxHorizontalOffset) * PercentMax, PercentMin, PercentMax);

    double IScrollProvider.HorizontalViewSize =>
        ComputeViewSize(_owner.AutomationExtentForTests.Width, _owner.AutomationViewportForTests.Width);

    bool IScrollProvider.VerticallyScrollable => MaxVerticalOffset > 0d;

    double IScrollProvider.VerticalScrollPercent =>
        MaxVerticalOffset <= 0d
            ? ScrollPatternIdentifiers.NoScroll
            : Math.Clamp((_owner.AutomationOffsetForTests.Y / MaxVerticalOffset) * PercentMax, PercentMin, PercentMax);

    double IScrollProvider.VerticalViewSize =>
        ComputeViewSize(_owner.AutomationExtentForTests.Height, _owner.AutomationViewportForTests.Height);

    void IScrollProvider.Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        var current = _owner.AutomationOffsetForTests;
        var targetX = current.X + ResolveScrollDelta(
            horizontalAmount,
            _owner.AutomationLineScrollSizeForTests.Width,
            _owner.AutomationPageScrollSizeForTests.Width);
        var targetY = current.Y + ResolveScrollDelta(
            verticalAmount,
            _owner.AutomationLineScrollSizeForTests.Height,
            _owner.AutomationPageScrollSizeForTests.Height);

        _owner.AutomationOffsetForTests = new Vector(targetX, targetY);
    }

    void IScrollProvider.SetScrollPercent(double horizontalPercent, double verticalPercent)
    {
        var current = _owner.AutomationOffsetForTests;
        var targetX = current.X;
        var targetY = current.Y;

        if (!double.IsNaN(horizontalPercent) && horizontalPercent != ScrollPatternIdentifiers.NoScroll)
        {
            targetX = MaxHorizontalOffset <= 0d
                ? current.X
                : Math.Clamp(horizontalPercent, PercentMin, PercentMax) / PercentMax * MaxHorizontalOffset;
        }

        if (!double.IsNaN(verticalPercent) && verticalPercent != ScrollPatternIdentifiers.NoScroll)
        {
            targetY = MaxVerticalOffset <= 0d
                ? current.Y
                : Math.Clamp(verticalPercent, PercentMin, PercentMax) / PercentMax * MaxVerticalOffset;
        }

        _owner.AutomationOffsetForTests = new Vector(targetX, targetY);
    }

    private double MaxHorizontalOffset =>
        Math.Max(0d, _owner.AutomationExtentForTests.Width - _owner.AutomationViewportForTests.Width);

    private double MaxVerticalOffset =>
        Math.Max(0d, _owner.AutomationExtentForTests.Height - _owner.AutomationViewportForTests.Height);

    private static double ComputeViewSize(double extent, double viewport)
    {
        if (extent <= 0d || viewport <= 0d)
        {
            return PercentMax;
        }

        return Math.Clamp(viewport / extent * PercentMax, PercentMin, PercentMax);
    }

    private static double ResolveScrollDelta(ScrollAmount amount, double smallStep, double largeStep)
    {
        var normalizedSmall = NormalizeStep(smallStep, fallback: 16d);
        var normalizedLarge = NormalizeStep(largeStep, fallback: normalizedSmall * 10d);

        return amount switch
        {
            ScrollAmount.LargeDecrement => -normalizedLarge,
            ScrollAmount.SmallDecrement => -normalizedSmall,
            ScrollAmount.LargeIncrement => normalizedLarge,
            ScrollAmount.SmallIncrement => normalizedSmall,
            _ => 0d
        };
    }

    private static double NormalizeStep(double value, double fallback)
    {
        return value > 0d ? value : fallback;
    }
}

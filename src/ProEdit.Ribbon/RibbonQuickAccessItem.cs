namespace ProEdit.Ribbon;

public sealed class RibbonQuickAccessItem
{
    public RibbonQuickAccessItem(IRibbonControl control)
    {
        Control = control ?? throw new ArgumentNullException(nameof(control));
    }

    public IRibbonControl Control { get; }
}

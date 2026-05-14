namespace ProEdit.Ribbon;

public interface IRibbonMenuEntry
{
}

public sealed class RibbonMenu
{
    public RibbonMenu(IReadOnlyList<IRibbonMenuEntry> items)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public IReadOnlyList<IRibbonMenuEntry> Items { get; }
}

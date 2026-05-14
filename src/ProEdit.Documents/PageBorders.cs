namespace ProEdit.Documents;

public sealed class PageBorders
{
    public BorderLine? Top { get; set; }
    public BorderLine? Bottom { get; set; }
    public BorderLine? Left { get; set; }
    public BorderLine? Right { get; set; }
    public PageBorderOffset OffsetFrom { get; set; } = PageBorderOffset.Page;
    public PageBorderDisplay Display { get; set; } = PageBorderDisplay.AllPages;
    public PageBorderZOrder ZOrder { get; set; } = PageBorderZOrder.Back;

    public bool HasAny =>
        Top is not null
        || Bottom is not null
        || Left is not null
        || Right is not null;

    public PageBorders Clone()
    {
        return new PageBorders
        {
            Top = Top?.Clone(),
            Bottom = Bottom?.Clone(),
            Left = Left?.Clone(),
            Right = Right?.Clone(),
            OffsetFrom = OffsetFrom,
            Display = Display,
            ZOrder = ZOrder
        };
    }
}

public enum PageBorderOffset
{
    Page,
    Text
}

public enum PageBorderDisplay
{
    AllPages,
    FirstPage,
    ExceptFirstPage
}

public enum PageBorderZOrder
{
    Front,
    Back
}

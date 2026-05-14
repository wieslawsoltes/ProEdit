namespace ProEdit.Documents;

public struct DocLineArrow
{
    public DocLineArrowType Type { get; set; }
    public DocLineArrowSize Width { get; set; }
    public DocLineArrowSize Length { get; set; }

    public bool IsVisible => Type != DocLineArrowType.None;
}

public enum DocLineArrowType
{
    None,
    Triangle,
    Stealth,
    Diamond,
    Oval,
    Arrow
}

public enum DocLineArrowSize
{
    Small,
    Medium,
    Large
}

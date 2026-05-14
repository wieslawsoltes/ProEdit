namespace ProEdit.Documents;

public sealed class ParagraphBorders
{
    public BorderLine? Top { get; set; }
    public BorderLine? Bottom { get; set; }
    public BorderLine? Left { get; set; }
    public BorderLine? Right { get; set; }

    public bool HasAny => Top is not null || Bottom is not null || Left is not null || Right is not null;

    public ParagraphBorders Clone()
    {
        return new ParagraphBorders
        {
            Top = Top?.Clone(),
            Bottom = Bottom?.Clone(),
            Left = Left?.Clone(),
            Right = Right?.Clone()
        };
    }
}

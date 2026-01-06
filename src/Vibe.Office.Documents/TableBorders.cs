namespace Vibe.Office.Documents;

public sealed class TableBorders
{
    public BorderLine? Top { get; set; }
    public BorderLine? Bottom { get; set; }
    public BorderLine? Left { get; set; }
    public BorderLine? Right { get; set; }
    public BorderLine? InsideHorizontal { get; set; }
    public BorderLine? InsideVertical { get; set; }

    public TableBorders Clone()
    {
        return new TableBorders
        {
            Top = Top?.Clone(),
            Bottom = Bottom?.Clone(),
            Left = Left?.Clone(),
            Right = Right?.Clone(),
            InsideHorizontal = InsideHorizontal?.Clone(),
            InsideVertical = InsideVertical?.Clone()
        };
    }
}

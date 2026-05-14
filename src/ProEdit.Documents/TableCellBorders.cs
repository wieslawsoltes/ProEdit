namespace ProEdit.Documents;

public sealed class TableCellBorders
{
    public BorderLine? Top { get; set; }
    public BorderLine? Bottom { get; set; }
    public BorderLine? Left { get; set; }
    public BorderLine? Right { get; set; }

    public TableCellBorders Clone()
    {
        return new TableCellBorders
        {
            Top = Top?.Clone(),
            Bottom = Bottom?.Clone(),
            Left = Left?.Clone(),
            Right = Right?.Clone()
        };
    }
}

using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class TableProperties
{
    public List<float> ColumnWidths { get; } = new List<float>();
    public DocThickness? CellPadding { get; set; }
    public TableBorders Borders { get; } = new TableBorders();
    public DocColor? ShadingColor { get; set; }
    public TableLook? Look { get; set; }

    public TableProperties Clone()
    {
        var clone = new TableProperties
        {
            CellPadding = CellPadding,
            ShadingColor = ShadingColor,
            Look = Look?.Clone()
        };
        clone.ColumnWidths.AddRange(ColumnWidths);
        clone.Borders.Top = Borders.Top?.Clone();
        clone.Borders.Bottom = Borders.Bottom?.Clone();
        clone.Borders.Left = Borders.Left?.Clone();
        clone.Borders.Right = Borders.Right?.Clone();
        clone.Borders.InsideHorizontal = Borders.InsideHorizontal?.Clone();
        clone.Borders.InsideVertical = Borders.InsideVertical?.Clone();
        return clone;
    }
}

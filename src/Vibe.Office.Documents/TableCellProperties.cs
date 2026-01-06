using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class TableCellProperties
{
    public DocThickness? Padding { get; set; }
    public DocColor? ShadingColor { get; set; }
    public TableCellBorders Borders { get; } = new TableCellBorders();
    public TableCellVerticalAlignment? VerticalAlignment { get; set; }

    public TableCellProperties Clone()
    {
        return new TableCellProperties
        {
            Padding = Padding,
            ShadingColor = ShadingColor,
            VerticalAlignment = VerticalAlignment,
            Borders =
            {
                Top = Borders.Top?.Clone(),
                Bottom = Borders.Bottom?.Clone(),
                Left = Borders.Left?.Clone(),
                Right = Borders.Right?.Clone()
            }
        };
    }
}

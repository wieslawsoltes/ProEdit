using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class TableCellProperties
{
    public DocThickness? Padding { get; set; }
    public DocColor? ShadingColor { get; set; }
    public TableCellBorders Borders { get; } = new TableCellBorders();
    public TableCellVerticalAlignment? VerticalAlignment { get; set; }
    public DocTextDirection? TextDirection { get; set; }
    public float? PreferredWidth { get; set; }
    public TableWidthUnit? PreferredWidthUnit { get; set; }

    public TableCellProperties Clone()
    {
        return new TableCellProperties
        {
            Padding = Padding,
            ShadingColor = ShadingColor,
            VerticalAlignment = VerticalAlignment,
            TextDirection = TextDirection,
            PreferredWidth = PreferredWidth,
            PreferredWidthUnit = PreferredWidthUnit,
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

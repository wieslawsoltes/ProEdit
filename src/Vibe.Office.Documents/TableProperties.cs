using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class TableProperties
{
    public List<float> ColumnWidths { get; } = new List<float>();
    public float? Width { get; set; }
    public TableWidthUnit? WidthUnit { get; set; }
    public float? Indent { get; set; }
    public TableWidthUnit? IndentUnit { get; set; }
    public TableAlignment? Alignment { get; set; }
    public TableLayoutMode? LayoutMode { get; set; }
    public float? CellSpacing { get; set; }
    public TableWidthUnit? CellSpacingUnit { get; set; }
    public DocThickness? CellPadding { get; set; }
    public TableBorders Borders { get; } = new TableBorders();
    public DocColor? ShadingColor { get; set; }
    public TableLook? Look { get; set; }

    public TableProperties Clone()
    {
        var clone = new TableProperties
        {
            Width = Width,
            WidthUnit = WidthUnit,
            Indent = Indent,
            IndentUnit = IndentUnit,
            Alignment = Alignment,
            LayoutMode = LayoutMode,
            CellSpacing = CellSpacing,
            CellSpacingUnit = CellSpacingUnit,
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

public enum TableAlignment
{
    Left,
    Center,
    Right
}

public enum TableWidthUnit
{
    Auto,
    Dxa,
    Pct
}

public enum TableLayoutMode
{
    Auto,
    Fixed
}

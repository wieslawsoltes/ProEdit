using ProEdit.Primitives;

namespace ProEdit.Documents;

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
    public FloatingAnchor? FloatingAnchor { get; set; }

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
            Look = Look?.Clone(),
            FloatingAnchor = FloatingAnchor is null ? null : CloneFloatingAnchor(FloatingAnchor)
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

    private static FloatingAnchor CloneFloatingAnchor(FloatingAnchor source)
    {
        return new FloatingAnchor
        {
            HorizontalReference = source.HorizontalReference,
            VerticalReference = source.VerticalReference,
            HorizontalAlignment = source.HorizontalAlignment,
            VerticalAlignment = source.VerticalAlignment,
            OffsetX = source.OffsetX,
            OffsetY = source.OffsetY,
            WrapStyle = source.WrapStyle,
            WrapSide = source.WrapSide,
            WrapPolygon = CloneWrapPolygon(source.WrapPolygon),
            BehindText = source.BehindText,
            AllowOverlap = source.AllowOverlap,
            ZOrder = source.ZOrder,
            Distance = source.Distance,
            AnchorOffset = source.AnchorOffset
        };
    }

    private static FloatingWrapPolygon? CloneWrapPolygon(FloatingWrapPolygon? polygon)
    {
        if (polygon is null)
        {
            return null;
        }

        var points = polygon.Points.ToArray();
        return new FloatingWrapPolygon(points);
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

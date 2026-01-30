using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class BorderLine
{
    public DocBorderStyle Style { get; set; } = DocBorderStyle.Single;
    public float Thickness { get; set; } = 1f;
    public DocColor Color { get; set; } = DocColor.Black;
    public float? Spacing { get; set; }
    public DocLineCap LineCap { get; set; } = DocLineCap.Flat;
    public DocLineJoin LineJoin { get; set; } = DocLineJoin.Miter;
    public float? MiterLimit { get; set; }
    public DocCompoundLine Compound { get; set; } = DocCompoundLine.Single;
    public float? CompoundSpacing { get; set; }
    public DocLineArrow HeadArrow { get; set; } = new DocLineArrow
    {
        Type = DocLineArrowType.None,
        Width = DocLineArrowSize.Medium,
        Length = DocLineArrowSize.Medium
    };
    public DocLineArrow TailArrow { get; set; } = new DocLineArrow
    {
        Type = DocLineArrowType.None,
        Width = DocLineArrowSize.Medium,
        Length = DocLineArrowSize.Medium
    };

    public bool IsVisible => Style != DocBorderStyle.None && Thickness > 0f;

    public BorderLine Clone()
    {
        return new BorderLine
        {
            Style = Style,
            Thickness = Thickness,
            Color = Color,
            Spacing = Spacing,
            LineCap = LineCap,
            LineJoin = LineJoin,
            MiterLimit = MiterLimit,
            Compound = Compound,
            CompoundSpacing = CompoundSpacing,
            HeadArrow = HeadArrow,
            TailArrow = TailArrow
        };
    }
}

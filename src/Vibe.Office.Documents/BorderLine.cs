using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class BorderLine
{
    public DocBorderStyle Style { get; set; } = DocBorderStyle.Single;
    public float Thickness { get; set; } = 1f;
    public DocColor Color { get; set; } = DocColor.Black;
    public float? Spacing { get; set; }

    public bool IsVisible => Style != DocBorderStyle.None && Thickness > 0f;

    public BorderLine Clone()
    {
        return new BorderLine
        {
            Style = Style,
            Thickness = Thickness,
            Color = Color,
            Spacing = Spacing
        };
    }
}

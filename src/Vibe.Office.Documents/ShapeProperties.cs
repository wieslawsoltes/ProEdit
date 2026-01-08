using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class ShapeProperties
{
    public string? PresetGeometry { get; set; }
    public DocColor? FillColor { get; set; }
    public BorderLine? Outline { get; set; }
    public float Rotation { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public DrawingEffects? Effects { get; set; }

    public ShapeProperties Clone()
    {
        return new ShapeProperties
        {
            PresetGeometry = PresetGeometry,
            FillColor = FillColor,
            Outline = Outline?.Clone(),
            Rotation = Rotation,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            Effects = Effects?.Clone()
        };
    }
}

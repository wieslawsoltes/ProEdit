using ProEdit.Primitives;

namespace ProEdit.Documents;

public sealed class ShapeProperties
{
    public string? PresetGeometry { get; set; }
    public DocColor? FillColor { get; set; }
    public ShapeFill? Fill { get; set; }
    public BorderLine? Outline { get; set; }
    public float Rotation { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public DrawingEffects? Effects { get; set; }
    public ShapeGeometry? CustomGeometry { get; set; }
    public Dictionary<string, double> AdjustValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ShapeProperties Clone()
    {
        var clone = new ShapeProperties
        {
            PresetGeometry = PresetGeometry,
            FillColor = FillColor,
            Fill = Fill?.Clone(),
            Outline = Outline?.Clone(),
            Rotation = Rotation,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            Effects = Effects?.Clone(),
            CustomGeometry = CustomGeometry?.Clone()
        };
        foreach (var pair in AdjustValues)
        {
            clone.AdjustValues[pair.Key] = pair.Value;
        }

        return clone;
    }
}

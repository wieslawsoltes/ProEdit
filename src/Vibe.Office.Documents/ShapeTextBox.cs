using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class ShapeTextBox
{
    public List<Block> Blocks { get; } = new List<Block>();
    public ShapeTextBoxProperties Properties { get; } = new ShapeTextBoxProperties();
}

public sealed class ShapeTextBoxProperties
{
    public DocThickness Padding { get; set; } = DocThickness.Uniform(0f);
    public ShapeTextVerticalAlignment VerticalAlignment { get; set; } = ShapeTextVerticalAlignment.Top;
}

public enum ShapeTextVerticalAlignment
{
    Top,
    Center,
    Bottom
}

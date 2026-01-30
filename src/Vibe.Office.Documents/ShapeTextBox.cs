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
    public ShapeTextAutoFit AutoFit { get; set; } = ShapeTextAutoFit.None;
    public ShapeTextOverflow HorizontalOverflow { get; set; } = ShapeTextOverflow.Overflow;
    public ShapeTextOverflow VerticalOverflow { get; set; } = ShapeTextOverflow.Overflow;
    public DocTextDirection? TextDirection { get; set; }
}

public enum ShapeTextVerticalAlignment
{
    Top,
    Center,
    Bottom
}

public enum ShapeTextAutoFit
{
    None,
    TextToFitShape,
    ShapeToFitText
}

public enum ShapeTextOverflow
{
    Overflow,
    Clip,
    Ellipsis
}

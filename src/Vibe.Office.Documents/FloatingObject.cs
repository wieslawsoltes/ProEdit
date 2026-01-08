using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class FloatingObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public Inline Content { get; }
    public FloatingAnchor Anchor { get; } = new FloatingAnchor();

    public FloatingObject(Inline content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }
}

public sealed class FloatingAnchor
{
    public FloatingHorizontalReference HorizontalReference { get; set; } = FloatingHorizontalReference.Margin;
    public FloatingVerticalReference VerticalReference { get; set; } = FloatingVerticalReference.Paragraph;
    public FloatingHorizontalAlignment HorizontalAlignment { get; set; } = FloatingHorizontalAlignment.None;
    public FloatingVerticalAlignment VerticalAlignment { get; set; } = FloatingVerticalAlignment.None;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public FloatingWrapStyle WrapStyle { get; set; } = FloatingWrapStyle.None;
    public FloatingWrapSide WrapSide { get; set; } = FloatingWrapSide.Both;
    public FloatingWrapPolygon? WrapPolygon { get; set; }
    public bool BehindText { get; set; }
    public DocThickness Distance { get; set; } = DocThickness.Uniform(0f);
    public int? AnchorOffset { get; set; }
}

public enum FloatingHorizontalReference
{
    Page,
    Margin,
    Column,
    Paragraph,
    Character
}

public enum FloatingVerticalReference
{
    Page,
    Margin,
    Paragraph,
    Line
}

public enum FloatingHorizontalAlignment
{
    None,
    Left,
    Center,
    Right,
    Inside,
    Outside
}

public enum FloatingVerticalAlignment
{
    None,
    Top,
    Center,
    Bottom,
    Inside,
    Outside
}

public enum FloatingWrapStyle
{
    None,
    Square,
    Tight,
    TopBottom,
    Through
}

public enum FloatingWrapSide
{
    Both,
    Left,
    Right,
    Largest
}

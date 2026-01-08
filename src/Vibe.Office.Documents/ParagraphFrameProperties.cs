namespace Vibe.Office.Documents;

public sealed class ParagraphFrameProperties
{
    public float? Width { get; set; }
    public float? Height { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public float? HorizontalSpace { get; set; }
    public float? VerticalSpace { get; set; }
    public FloatingHorizontalReference? HorizontalReference { get; set; }
    public FloatingVerticalReference? VerticalReference { get; set; }
    public FloatingHorizontalAlignment? HorizontalAlignment { get; set; }
    public FloatingVerticalAlignment? VerticalAlignment { get; set; }
    public FloatingWrapStyle? WrapStyle { get; set; }
    public FloatingWrapSide? WrapSide { get; set; }
    public bool? AnchorLock { get; set; }

    public bool HasValues =>
        Width.HasValue
        || Height.HasValue
        || X.HasValue
        || Y.HasValue
        || HorizontalSpace.HasValue
        || VerticalSpace.HasValue
        || HorizontalReference.HasValue
        || VerticalReference.HasValue
        || HorizontalAlignment.HasValue
        || VerticalAlignment.HasValue
        || WrapStyle.HasValue
        || WrapSide.HasValue
        || AnchorLock.HasValue;

    public ParagraphFrameProperties Clone()
    {
        return new ParagraphFrameProperties
        {
            Width = Width,
            Height = Height,
            X = X,
            Y = Y,
            HorizontalSpace = HorizontalSpace,
            VerticalSpace = VerticalSpace,
            HorizontalReference = HorizontalReference,
            VerticalReference = VerticalReference,
            HorizontalAlignment = HorizontalAlignment,
            VerticalAlignment = VerticalAlignment,
            WrapStyle = WrapStyle,
            WrapSide = WrapSide,
            AnchorLock = AnchorLock
        };
    }
}

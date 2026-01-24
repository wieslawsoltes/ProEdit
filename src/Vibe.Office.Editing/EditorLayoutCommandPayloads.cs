using Vibe.Office.Documents;

namespace Vibe.Office.Editing;

public readonly record struct EditorPageMarginsRequest(
    float Left,
    float Top,
    float Right,
    float Bottom,
    float? HeaderOffset = null,
    float? FooterOffset = null,
    float? Gutter = null,
    bool? MirrorMargins = null,
    bool? GutterAtTop = null);

public readonly record struct EditorPageSizeRequest(
    float Width,
    float Height,
    PageOrientation? Orientation = null);

public readonly record struct EditorPageOrientationRequest(PageOrientation Orientation);

public readonly record struct EditorColumnLayoutRequest(
    int ColumnCount,
    float? ColumnGap = null,
    bool? Separator = null,
    bool? EqualWidth = null);

public enum EditorBreakKind
{
    Page,
    Column,
    SectionNextPage,
    SectionContinuous,
    SectionEvenPage,
    SectionOddPage,
    SectionNextColumn
}

public readonly record struct EditorBreakRequest(EditorBreakKind Kind);

public enum EditorFloatingPositionKind
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public readonly record struct EditorFloatingPositionRequest(EditorFloatingPositionKind Kind);

public enum EditorFloatingWrapKind
{
    Square,
    Tight,
    Through,
    TopBottom,
    BehindText,
    InFrontOfText
}

public readonly record struct EditorFloatingWrapRequest(EditorFloatingWrapKind Kind);

public readonly record struct EditorFloatingWrapSideRequest(FloatingWrapSide Side);

public enum EditorFloatingAlignKind
{
    Left,
    Center,
    Right,
    Top,
    Middle,
    Bottom
}

public enum EditorFloatingAlignTarget
{
    Margin,
    Page,
    SelectedObjects
}

public readonly record struct EditorFloatingAlignRequest(
    EditorFloatingAlignKind Kind,
    EditorFloatingAlignTarget Target = EditorFloatingAlignTarget.Margin);

public enum EditorFloatingOrderKind
{
    BringForward,
    BringToFront,
    SendBackward,
    SendToBack
}

public readonly record struct EditorFloatingOrderRequest(EditorFloatingOrderKind Kind);

public enum EditorFloatingRotateKind
{
    RotateRight90,
    RotateLeft90,
    FlipHorizontal,
    FlipVertical
}

public readonly record struct EditorFloatingRotateRequest(EditorFloatingRotateKind Kind);

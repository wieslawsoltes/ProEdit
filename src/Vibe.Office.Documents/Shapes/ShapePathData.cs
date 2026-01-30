namespace Vibe.Office.Documents;

public sealed class ShapePathData
{
    public ShapePathFillMode FillMode { get; }
    public bool IsStroked { get; }
    public bool IsFilled { get; }
    public List<ShapePathSegment> Segments { get; } = new List<ShapePathSegment>();

    public ShapePathData(ShapePathFillMode fillMode, bool isStroked, bool isFilled)
    {
        FillMode = fillMode;
        IsStroked = isStroked;
        IsFilled = isFilled;
    }
}

public enum ShapePathSegmentKind
{
    MoveTo,
    LineTo,
    QuadTo,
    CubicTo,
    ArcTo,
    Close
}

public readonly struct ShapePathSegment
{
    public ShapePathSegmentKind Kind { get; }
    public float X1 { get; }
    public float Y1 { get; }
    public float X2 { get; }
    public float Y2 { get; }
    public float X3 { get; }
    public float Y3 { get; }
    public float StartAngle { get; }
    public float SweepAngle { get; }

    private ShapePathSegment(
        ShapePathSegmentKind kind,
        float x1,
        float y1,
        float x2,
        float y2,
        float x3,
        float y3,
        float startAngle,
        float sweepAngle)
    {
        Kind = kind;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        X3 = x3;
        Y3 = y3;
        StartAngle = startAngle;
        SweepAngle = sweepAngle;
    }

    public static ShapePathSegment MoveTo(float x, float y)
    {
        return new ShapePathSegment(ShapePathSegmentKind.MoveTo, x, y, 0f, 0f, 0f, 0f, 0f, 0f);
    }

    public static ShapePathSegment LineTo(float x, float y)
    {
        return new ShapePathSegment(ShapePathSegmentKind.LineTo, x, y, 0f, 0f, 0f, 0f, 0f, 0f);
    }

    public static ShapePathSegment QuadTo(float cx, float cy, float x, float y)
    {
        return new ShapePathSegment(ShapePathSegmentKind.QuadTo, cx, cy, x, y, 0f, 0f, 0f, 0f);
    }

    public static ShapePathSegment CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
    {
        return new ShapePathSegment(ShapePathSegmentKind.CubicTo, c1x, c1y, c2x, c2y, x, y, 0f, 0f);
    }

    public static ShapePathSegment ArcTo(float x, float y, float width, float height, float startAngle, float sweepAngle)
    {
        return new ShapePathSegment(ShapePathSegmentKind.ArcTo, x, y, width, height, 0f, 0f, startAngle, sweepAngle);
    }

    public static ShapePathSegment Close()
    {
        return new ShapePathSegment(ShapePathSegmentKind.Close, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
    }
}

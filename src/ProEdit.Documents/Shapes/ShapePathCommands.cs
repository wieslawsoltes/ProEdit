namespace ProEdit.Documents;

public abstract class ShapePathCommand
{
    internal abstract void Append(
        ShapePathData data,
        ShapeGeometryContext context,
        float scaleX,
        float scaleY,
        ref ShapePoint current,
        ref ShapePoint subPathStart);

    public abstract ShapePathCommand Clone();
}

public sealed class ShapeMoveToCommand : ShapePathCommand
{
    public ShapeAdjustPoint Point { get; }

    public ShapeMoveToCommand(ShapeAdjustPoint point)
    {
        Point = point ?? throw new ArgumentNullException(nameof(point));
    }

    internal override void Append(ShapePathData data, ShapeGeometryContext context, float scaleX, float scaleY, ref ShapePoint current, ref ShapePoint subPathStart)
    {
        var x = context.GetValue(Point.X);
        var y = context.GetValue(Point.Y);
        current = new ShapePoint(x, y);
        subPathStart = current;
        data.Segments.Add(ShapePathSegment.MoveTo((float)(x * scaleX), (float)(y * scaleY)));
    }

    public override ShapePathCommand Clone() => new ShapeMoveToCommand(Point.Clone());
}

public sealed class ShapeLineToCommand : ShapePathCommand
{
    public ShapeAdjustPoint Point { get; }

    public ShapeLineToCommand(ShapeAdjustPoint point)
    {
        Point = point ?? throw new ArgumentNullException(nameof(point));
    }

    internal override void Append(ShapePathData data, ShapeGeometryContext context, float scaleX, float scaleY, ref ShapePoint current, ref ShapePoint subPathStart)
    {
        var x = context.GetValue(Point.X);
        var y = context.GetValue(Point.Y);
        current = new ShapePoint(x, y);
        data.Segments.Add(ShapePathSegment.LineTo((float)(x * scaleX), (float)(y * scaleY)));
    }

    public override ShapePathCommand Clone() => new ShapeLineToCommand(Point.Clone());
}

public sealed class ShapeQuadBezierToCommand : ShapePathCommand
{
    public ShapeAdjustPoint ControlPoint { get; }
    public ShapeAdjustPoint EndPoint { get; }

    public ShapeQuadBezierToCommand(ShapeAdjustPoint controlPoint, ShapeAdjustPoint endPoint)
    {
        ControlPoint = controlPoint ?? throw new ArgumentNullException(nameof(controlPoint));
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
    }

    internal override void Append(ShapePathData data, ShapeGeometryContext context, float scaleX, float scaleY, ref ShapePoint current, ref ShapePoint subPathStart)
    {
        var cx = context.GetValue(ControlPoint.X);
        var cy = context.GetValue(ControlPoint.Y);
        var x = context.GetValue(EndPoint.X);
        var y = context.GetValue(EndPoint.Y);
        current = new ShapePoint(x, y);
        data.Segments.Add(ShapePathSegment.QuadTo(
            (float)(cx * scaleX),
            (float)(cy * scaleY),
            (float)(x * scaleX),
            (float)(y * scaleY)));
    }

    public override ShapePathCommand Clone() => new ShapeQuadBezierToCommand(ControlPoint.Clone(), EndPoint.Clone());
}

public sealed class ShapeCubicBezierToCommand : ShapePathCommand
{
    public ShapeAdjustPoint ControlPoint1 { get; }
    public ShapeAdjustPoint ControlPoint2 { get; }
    public ShapeAdjustPoint EndPoint { get; }

    public ShapeCubicBezierToCommand(ShapeAdjustPoint controlPoint1, ShapeAdjustPoint controlPoint2, ShapeAdjustPoint endPoint)
    {
        ControlPoint1 = controlPoint1 ?? throw new ArgumentNullException(nameof(controlPoint1));
        ControlPoint2 = controlPoint2 ?? throw new ArgumentNullException(nameof(controlPoint2));
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
    }

    internal override void Append(ShapePathData data, ShapeGeometryContext context, float scaleX, float scaleY, ref ShapePoint current, ref ShapePoint subPathStart)
    {
        var c1x = context.GetValue(ControlPoint1.X);
        var c1y = context.GetValue(ControlPoint1.Y);
        var c2x = context.GetValue(ControlPoint2.X);
        var c2y = context.GetValue(ControlPoint2.Y);
        var x = context.GetValue(EndPoint.X);
        var y = context.GetValue(EndPoint.Y);
        current = new ShapePoint(x, y);
        data.Segments.Add(ShapePathSegment.CubicTo(
            (float)(c1x * scaleX),
            (float)(c1y * scaleY),
            (float)(c2x * scaleX),
            (float)(c2y * scaleY),
            (float)(x * scaleX),
            (float)(y * scaleY)));
    }

    public override ShapePathCommand Clone() => new ShapeCubicBezierToCommand(ControlPoint1.Clone(), ControlPoint2.Clone(), EndPoint.Clone());
}

public sealed class ShapeArcToCommand : ShapePathCommand
{
    public string RadiusX { get; }
    public string RadiusY { get; }
    public string StartAngle { get; }
    public string SweepAngle { get; }

    public ShapeArcToCommand(string radiusX, string radiusY, string startAngle, string sweepAngle)
    {
        RadiusX = radiusX;
        RadiusY = radiusY;
        StartAngle = startAngle;
        SweepAngle = sweepAngle;
    }

    internal override void Append(ShapePathData data, ShapeGeometryContext context, float scaleX, float scaleY, ref ShapePoint current, ref ShapePoint subPathStart)
    {
        var rx = context.GetValue(RadiusX);
        var ry = context.GetValue(RadiusY);
        if (rx <= 0d || ry <= 0d)
        {
            return;
        }

        var ooStart = context.GetValue(StartAngle) / ShapeGeometryContext.OoxmlDegreeToDegrees;
        var ooSweep = context.GetValue(SweepAngle) / ShapeGeometryContext.OoxmlDegreeToDegrees;

        var awtStart = ConvertOoxmlToArcAngle(ooStart, rx, ry);
        var awtSweep = ConvertOoxmlToArcAngle(ooStart + ooSweep, rx, ry) - awtStart;

        var radStart = DegreesToRadians(ooStart);
        var invStart = Math.Atan2(rx * Math.Sin(radStart), ry * Math.Cos(radStart));
        var centerX = current.X - rx * Math.Cos(invStart);
        var centerY = current.Y - ry * Math.Sin(invStart);
        var rectX = centerX - rx;
        var rectY = centerY - ry;

        var rectWidth = rx * 2d;
        var rectHeight = ry * 2d;

        data.Segments.Add(ShapePathSegment.ArcTo(
            (float)(rectX * scaleX),
            (float)(rectY * scaleY),
            (float)(rectWidth * scaleX),
            (float)(rectHeight * scaleY),
            (float)awtStart,
            (float)awtSweep));

        var radEnd = DegreesToRadians(ooStart + ooSweep);
        var invEnd = Math.Atan2(rx * Math.Sin(radEnd), ry * Math.Cos(radEnd));
        var endX = centerX + rx * Math.Cos(invEnd);
        var endY = centerY + ry * Math.Sin(invEnd);
        current = new ShapePoint(endX, endY);
    }

    public override ShapePathCommand Clone() => new ShapeArcToCommand(RadiusX, RadiusY, StartAngle, SweepAngle);

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180d);
    }

    private static double ConvertOoxmlToArcAngle(double ooxmlAngle, double radiusX, double radiusY)
    {
        var aspect = radiusY / radiusX;
        var awtAngle = -ooxmlAngle;
        var awtAngle2 = awtAngle % 360d;
        var awtAngle3 = awtAngle - awtAngle2;
        switch ((int)(awtAngle2 / 90d))
        {
            case -3:
                awtAngle3 -= 360d;
                awtAngle2 += 360d;
                break;
            case -2:
            case -1:
                awtAngle3 -= 180d;
                awtAngle2 += 180d;
                break;
            case 2:
            case 1:
                awtAngle3 += 180d;
                awtAngle2 -= 180d;
                break;
            case 3:
                awtAngle3 += 360d;
                awtAngle2 -= 360d;
                break;
        }

        var skew = Math.Atan2(Math.Tan(DegreesToRadians(awtAngle2)), aspect);
        return RadiansToDegrees(skew) + awtAngle3;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * (180d / Math.PI);
    }
}

public sealed class ShapeClosePathCommand : ShapePathCommand
{
    internal override void Append(ShapePathData data, ShapeGeometryContext context, float scaleX, float scaleY, ref ShapePoint current, ref ShapePoint subPathStart)
    {
        data.Segments.Add(ShapePathSegment.Close());
        current = subPathStart;
    }

    public override ShapePathCommand Clone() => new ShapeClosePathCommand();
}

internal readonly struct ShapePoint
{
    public double X { get; }
    public double Y { get; }

    public ShapePoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

using ProEdit.Primitives;

namespace ProEdit.Documents;

public sealed class FloatingWrapPolygon
{
    private readonly DocPoint[] _points;

    public FloatingWrapPolygon(DocPoint[] points)
    {
        if (points is null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        _points = points;
        Bounds = ComputeBounds(points);
    }

    public ReadOnlySpan<DocPoint> Points => _points;

    public DocRect Bounds { get; }

    private static DocRect ComputeBounds(ReadOnlySpan<DocPoint> points)
    {
        if (points.IsEmpty)
        {
            return new DocRect(0f, 0f, 0f, 0f);
        }

        var minX = points[0].X;
        var maxX = points[0].X;
        var minY = points[0].Y;
        var maxY = points[0].Y;
        for (var i = 1; i < points.Length; i++)
        {
            var point = points[i];
            if (point.X < minX)
            {
                minX = point.X;
            }

            if (point.X > maxX)
            {
                maxX = point.X;
            }

            if (point.Y < minY)
            {
                minY = point.Y;
            }

            if (point.Y > maxY)
            {
                maxY = point.Y;
            }
        }

        return new DocRect(minX, minY, MathF.Max(0f, maxX - minX), MathF.Max(0f, maxY - minY));
    }
}

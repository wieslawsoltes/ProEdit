using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public sealed class FloatingWrapContour
{
    private readonly DocPoint[] _points;

    public FloatingWrapContour(DocPoint[] points)
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

    public bool TryGetHorizontalSpan(float y, out float left, out float right)
    {
        left = 0f;
        right = 0f;
        if (_points.Length < 3)
        {
            return false;
        }

        if (y < Bounds.Y || y > Bounds.Bottom)
        {
            return false;
        }

        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var hits = 0;
        var previous = _points[^1];
        for (var i = 0; i < _points.Length; i++)
        {
            var current = _points[i];
            var y1 = previous.Y;
            var y2 = current.Y;
            if (MathF.Abs(y2 - y1) < 0.0001f)
            {
                previous = current;
                continue;
            }

            var minY = MathF.Min(y1, y2);
            var maxY = MathF.Max(y1, y2);
            if (y < minY || y >= maxY)
            {
                previous = current;
                continue;
            }

            var t = (y - y1) / (y2 - y1);
            var x = previous.X + t * (current.X - previous.X);
            if (x < minX)
            {
                minX = x;
            }

            if (x > maxX)
            {
                maxX = x;
            }

            hits++;
            previous = current;
        }

        if (hits < 2)
        {
            return false;
        }

        left = minX;
        right = maxX;
        return right >= left;
    }

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

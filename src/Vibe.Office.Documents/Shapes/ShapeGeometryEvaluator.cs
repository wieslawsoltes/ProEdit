using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public static class ShapeGeometryEvaluator
{
    public static ShapeGeometry? ResolveGeometry(ShapeProperties properties)
    {
        if (properties.CustomGeometry is not null)
        {
            return properties.CustomGeometry;
        }

        if (!string.IsNullOrWhiteSpace(properties.PresetGeometry)
            && ShapePresetGeometryLibrary.TryGetGeometry(properties.PresetGeometry, out var preset))
        {
            return preset;
        }

        return null;
    }

    public static IReadOnlyList<ShapePathData> Evaluate(ShapeProperties properties, float width, float height)
    {
        var geometry = ResolveGeometry(properties) ?? ShapeGeometry.CreateRectangle();
        return EvaluateGeometry(geometry, properties, width, height);
    }

    public static IReadOnlyList<ShapePathData> EvaluateGeometry(ShapeGeometry geometry, ShapeProperties? properties, float width, float height)
    {
        if (geometry.Paths.Count == 0)
        {
            return Array.Empty<ShapePathData>();
        }

        var list = new List<ShapePathData>(geometry.Paths.Count);
        foreach (var path in geometry.Paths)
        {
            list.Add(EvaluatePath(path, geometry, properties, width, height));
        }

        return list;
    }

    public static DocRect ResolveTextRectangle(ShapeProperties properties, float width, float height)
    {
        var geometry = ResolveGeometry(properties);
        if (geometry?.TextRectangle is null)
        {
            return new DocRect(0f, 0f, width, height);
        }

        var context = new ShapeGeometryContext(geometry, properties, width, height);
        var textRect = geometry.TextRectangle;
        var left = (float)context.GetValue(textRect.Left);
        var top = (float)context.GetValue(textRect.Top);
        var right = (float)context.GetValue(textRect.Right);
        var bottom = (float)context.GetValue(textRect.Bottom);

        left = MathF.Max(0f, left);
        top = MathF.Max(0f, top);
        right = MathF.Min(width, right);
        bottom = MathF.Min(height, bottom);

        if (right <= left || bottom <= top)
        {
            return new DocRect(0f, 0f, width, height);
        }

        return new DocRect(left, top, right - left, bottom - top);
    }

    private static ShapePathData EvaluatePath(ShapePath path, ShapeGeometry geometry, ShapeProperties? properties, float width, float height)
    {
        var pathWidth = path.Width > 0 ? path.Width : width;
        var pathHeight = path.Height > 0 ? path.Height : height;
        if (pathWidth <= 0 || pathHeight <= 0)
        {
            pathWidth = Math.Max(1f, width);
            pathHeight = Math.Max(1f, height);
        }

        var scaleX = width <= 0f ? 1f : width / pathWidth;
        var scaleY = height <= 0f ? 1f : height / pathHeight;

        var context = new ShapeGeometryContext(geometry, properties, pathWidth, pathHeight);
        var data = new ShapePathData(path.FillMode, path.FillRule, path.IsStroked, path.IsFilled);
        var current = new ShapePoint(0d, 0d);
        var subPathStart = current;
        foreach (var command in path.Commands)
        {
            command.Append(data, context, scaleX, scaleY, ref current, ref subPathStart);
        }

        return data;
    }
}

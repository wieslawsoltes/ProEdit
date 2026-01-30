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
        var data = new ShapePathData(path.FillMode, path.IsStroked, path.IsFilled);
        var current = new ShapePoint(0d, 0d);
        var subPathStart = current;
        foreach (var command in path.Commands)
        {
            command.Append(data, context, scaleX, scaleY, ref current, ref subPathStart);
        }

        return data;
    }
}

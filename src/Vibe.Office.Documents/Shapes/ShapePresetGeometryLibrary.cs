using System.Xml.Linq;

namespace Vibe.Office.Documents;

internal static class ShapePresetGeometryLibrary
{
    private static readonly Lazy<ShapePresetGeometryCache> Cache = new(LoadGeometries);

    public static bool TryGetGeometry(string preset, out ShapeGeometry geometry)
    {
        geometry = null!;
        if (string.IsNullOrWhiteSpace(preset))
        {
            return false;
        }

        var key = preset.Trim();
        var cache = Cache.Value;
        if (cache.Geometries.TryGetValue(key, out var resolved) && resolved is not null)
        {
            geometry = resolved;
            return true;
        }

        var normalized = NormalizePresetKey(key);
        if (cache.Geometries.TryGetValue(normalized, out resolved) && resolved is not null)
        {
            geometry = resolved;
            return true;
        }

        return false;
    }

    internal static IReadOnlyList<string> GetPresetNames()
    {
        return Cache.Value.PresetNames;
    }

    private static ShapePresetGeometryCache LoadGeometries()
    {
        var dict = new Dictionary<string, ShapeGeometry>(StringComparer.OrdinalIgnoreCase);
        var presetNames = new List<string>();
        var assembly = typeof(ShapePresetGeometryLibrary).Assembly;
        using var stream = assembly.GetManifestResourceStream("Vibe.Office.Documents.Shapes.presetShapeDefinitions.xml");
        if (stream is null)
        {
            return new ShapePresetGeometryCache(dict, presetNames);
        }

        var document = XDocument.Load(stream, LoadOptions.None);
        var root = document.Root;
        if (root is null)
        {
            return new ShapePresetGeometryCache(dict, presetNames);
        }

        XNamespace ns = "http://schemas.openxmlformats.org/drawingml/2006/main";
        foreach (var element in root.Elements())
        {
            var name = element.Name.LocalName;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            presetNames.Add(name);
            var geometry = new ShapeGeometry();
            ParseGuideList(element.Element(ns + "avLst"), geometry.Adjusts);
            ParseGuideList(element.Element(ns + "gdLst"), geometry.Guides);
            geometry.TextRectangle = ParseTextRectangle(element.Element(ns + "rect"));
            ParsePathList(element.Element(ns + "pathLst"), geometry.Paths, ns);

            dict[name] = geometry;
            var normalized = NormalizePresetKey(name);
            if (!dict.ContainsKey(normalized))
            {
                dict[normalized] = geometry;
            }
        }

        return new ShapePresetGeometryCache(dict, presetNames);
    }

    private static void ParseGuideList(XElement? list, ICollection<ShapeGuide> target)
    {
        if (list is null)
        {
            return;
        }

        foreach (var gd in list.Elements())
        {
            if (!gd.Name.LocalName.Equals("gd", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = (string?)gd.Attribute("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var formula = (string?)gd.Attribute("fmla");
            target.Add(new ShapeGuide(name, formula));
        }
    }

    private static ShapeTextRectangle? ParseTextRectangle(XElement? rect)
    {
        if (rect is null)
        {
            return null;
        }

        var left = (string?)rect.Attribute("l") ?? "l";
        var top = (string?)rect.Attribute("t") ?? "t";
        var right = (string?)rect.Attribute("r") ?? "r";
        var bottom = (string?)rect.Attribute("b") ?? "b";
        return new ShapeTextRectangle(left, top, right, bottom);
    }

    private static void ParsePathList(XElement? list, ICollection<ShapePath> paths, XNamespace ns)
    {
        if (list is null)
        {
            return;
        }

        foreach (var pathElement in list.Elements(ns + "path"))
        {
            var path = new ShapePath();
            var width = (long?)pathElement.Attribute("w");
            var height = (long?)pathElement.Attribute("h");
            path.Width = width ?? -1;
            path.Height = height ?? -1;
            path.FillMode = ParseFillMode((string?)pathElement.Attribute("fill"));
            path.IsStroked = ParseBool((string?)pathElement.Attribute("stroke"), true);
            path.IsExtrusionOk = ParseBool((string?)pathElement.Attribute("extrusionOk"), false);

            foreach (var command in pathElement.Elements())
            {
                switch (command.Name.LocalName)
                {
                    case "close":
                        path.Commands.Add(new ShapeClosePathCommand());
                        break;
                    case "moveTo":
                        if (TryParsePoint(command, ns, out var movePoint))
                        {
                            path.Commands.Add(new ShapeMoveToCommand(movePoint));
                        }
                        break;
                    case "lnTo":
                        if (TryParsePoint(command, ns, out var linePoint))
                        {
                            path.Commands.Add(new ShapeLineToCommand(linePoint));
                        }
                        break;
                    case "quadBezTo":
                    {
                        var points = ParsePoints(command, ns, 2);
                        if (points is not null)
                        {
                            path.Commands.Add(new ShapeQuadBezierToCommand(points[0], points[1]));
                        }
                        break;
                    }
                    case "cubicBezTo":
                    {
                        var points = ParsePoints(command, ns, 3);
                        if (points is not null)
                        {
                            path.Commands.Add(new ShapeCubicBezierToCommand(points[0], points[1], points[2]));
                        }
                        break;
                    }
                    case "arcTo":
                    {
                        var wR = (string?)command.Attribute("wR");
                        var hR = (string?)command.Attribute("hR");
                        var stAng = (string?)command.Attribute("stAng");
                        var swAng = (string?)command.Attribute("swAng");
                        if (!string.IsNullOrWhiteSpace(wR) && !string.IsNullOrWhiteSpace(hR)
                            && !string.IsNullOrWhiteSpace(stAng) && !string.IsNullOrWhiteSpace(swAng))
                        {
                            path.Commands.Add(new ShapeArcToCommand(wR, hR, stAng, swAng));
                        }
                        break;
                    }
                }
            }

            paths.Add(path);
        }
    }

    private static bool TryParsePoint(XElement command, XNamespace ns, out ShapeAdjustPoint point)
    {
        point = null!;
        var pt = command.Element(ns + "pt");
        if (pt is null)
        {
            return false;
        }

        var x = (string?)pt.Attribute("x");
        var y = (string?)pt.Attribute("y");
        if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y))
        {
            return false;
        }

        point = new ShapeAdjustPoint(x, y);
        return true;
    }

    private static ShapeAdjustPoint[]? ParsePoints(XElement command, XNamespace ns, int expected)
    {
        var points = new List<ShapeAdjustPoint>(expected);
        foreach (var pt in command.Elements(ns + "pt"))
        {
            var x = (string?)pt.Attribute("x");
            var y = (string?)pt.Attribute("y");
            if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y))
            {
                continue;
            }

            points.Add(new ShapeAdjustPoint(x, y));
            if (points.Count == expected)
            {
                break;
            }
        }

        return points.Count == expected ? points.ToArray() : null;
    }

    private static ShapePathFillMode ParseFillMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ShapePathFillMode.Normal;
        }

        return value switch
        {
            "none" => ShapePathFillMode.None,
            "lighten" => ShapePathFillMode.Lighten,
            "lightenLess" => ShapePathFillMode.LightenLess,
            "darken" => ShapePathFillMode.Darken,
            "darkenLess" => ShapePathFillMode.DarkenLess,
            _ => ShapePathFillMode.Normal
        };
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static string NormalizePresetKey(string value)
    {
        var span = value.AsSpan().Trim();
        if (span.IsEmpty)
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[span.Length];
        var count = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var ch = span[i];
            if (!char.IsLetterOrDigit(ch))
            {
                continue;
            }

            buffer[count++] = char.ToLowerInvariant(ch);
        }

        var normalized = new string(buffer.Slice(0, count));
        if (normalized.EndsWith("rectangle", StringComparison.Ordinal))
        {
            normalized = normalized[..^"rectangle".Length] + "rect";
        }

        return normalized;
    }

    private sealed class ShapePresetGeometryCache
    {
        public ShapePresetGeometryCache(
            Dictionary<string, ShapeGeometry> geometries,
            List<string> presetNames)
        {
            Geometries = geometries;
            PresetNames = presetNames.AsReadOnly();
        }

        public Dictionary<string, ShapeGeometry> Geometries { get; }

        public IReadOnlyList<string> PresetNames { get; }
    }
}

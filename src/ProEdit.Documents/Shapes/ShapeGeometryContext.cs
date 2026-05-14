using System.Globalization;

namespace ProEdit.Documents;

public sealed class ShapeGeometryContext
{
    public const double OoxmlDegreeToDegrees = 60000d;

    private readonly Dictionary<string, double> _values;
    private readonly double _width;
    private readonly double _height;

    public ShapeGeometryContext(ShapeGeometry geometry, ShapeProperties? properties, double width, double height)
    {
        _width = width;
        _height = height;
        _values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (properties is not null)
        {
            foreach (var pair in properties.AdjustValues)
            {
                _values[pair.Key] = pair.Value;
            }
        }

        foreach (var adjust in geometry.Adjusts)
        {
            if (_values.ContainsKey(adjust.Name))
            {
                continue;
            }

            _values[adjust.Name] = adjust.Evaluate(this);
        }

        foreach (var guide in geometry.Guides)
        {
            _values[guide.Name] = guide.Evaluate(this);
        }
    }

    public double GetValue(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0d;
        }

        if (TryParseNumber(token, out var numeric))
        {
            return numeric;
        }

        if (_values.TryGetValue(token, out var value))
        {
            return value;
        }

        if (TryGetBuiltInValue(token, out var builtIn))
        {
            return builtIn;
        }

        return 0d;
    }

    private static bool TryParseNumber(string token, out double value)
    {
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private bool TryGetBuiltInValue(string token, out double value)
    {
        var key = token.AsSpan();
        var width = _width;
        var height = _height;
        var shortSide = Math.Min(width, height);
        var longSide = Math.Max(width, height);

        switch (key)
        {
            case "3cd4":
                value = 270d * OoxmlDegreeToDegrees;
                return true;
            case "3cd8":
                value = 135d * OoxmlDegreeToDegrees;
                return true;
            case "5cd8":
                value = 225d * OoxmlDegreeToDegrees;
                return true;
            case "7cd8":
                value = 315d * OoxmlDegreeToDegrees;
                return true;
            case "cd2":
                value = 180d * OoxmlDegreeToDegrees;
                return true;
            case "cd4":
                value = 90d * OoxmlDegreeToDegrees;
                return true;
            case "cd8":
                value = 45d * OoxmlDegreeToDegrees;
                return true;
            case "l":
                value = 0d;
                return true;
            case "t":
                value = 0d;
                return true;
            case "r":
                value = width;
                return true;
            case "b":
                value = height;
                return true;
            case "hc":
                value = width / 2d;
                return true;
            case "vc":
                value = height / 2d;
                return true;
            case "w":
                value = width;
                return true;
            case "h":
                value = height;
                return true;
            case "wd2":
                value = width / 2d;
                return true;
            case "wd3":
                value = width / 3d;
                return true;
            case "wd4":
                value = width / 4d;
                return true;
            case "wd5":
                value = width / 5d;
                return true;
            case "wd6":
                value = width / 6d;
                return true;
            case "wd8":
                value = width / 8d;
                return true;
            case "wd10":
                value = width / 10d;
                return true;
            case "wd32":
                value = width / 32d;
                return true;
            case "hd2":
                value = height / 2d;
                return true;
            case "hd3":
                value = height / 3d;
                return true;
            case "hd4":
                value = height / 4d;
                return true;
            case "hd5":
                value = height / 5d;
                return true;
            case "hd6":
                value = height / 6d;
                return true;
            case "hd8":
                value = height / 8d;
                return true;
            case "ss":
                value = shortSide;
                return true;
            case "ls":
                value = longSide;
                return true;
            case "ssd2":
                value = shortSide / 2d;
                return true;
            case "ssd4":
                value = shortSide / 4d;
                return true;
            case "ssd6":
                value = shortSide / 6d;
                return true;
            case "ssd8":
                value = shortSide / 8d;
                return true;
            case "ssd16":
                value = shortSide / 16d;
                return true;
            case "ssd32":
                value = shortSide / 32d;
                return true;
            default:
                value = 0d;
                return false;
        }
    }
}

namespace ProEdit.Documents;

public sealed class ShapeGeometry
{
    public List<ShapeGuide> Adjusts { get; } = new List<ShapeGuide>();
    public List<ShapeGuide> Guides { get; } = new List<ShapeGuide>();
    public List<ShapePath> Paths { get; } = new List<ShapePath>();
    public ShapeTextRectangle? TextRectangle { get; set; }

    public ShapeGeometry Clone()
    {
        var clone = new ShapeGeometry();
        foreach (var adjust in Adjusts)
        {
            clone.Adjusts.Add(adjust.Clone());
        }

        foreach (var guide in Guides)
        {
            clone.Guides.Add(guide.Clone());
        }

        foreach (var path in Paths)
        {
            clone.Paths.Add(path.Clone());
        }

        clone.TextRectangle = TextRectangle?.Clone();
        return clone;
    }

    public static ShapeGeometry CreateRectangle()
    {
        var geometry = new ShapeGeometry();
        var path = new ShapePath();
        path.Commands.Add(new ShapeMoveToCommand(new ShapeAdjustPoint("l", "t")));
        path.Commands.Add(new ShapeLineToCommand(new ShapeAdjustPoint("r", "t")));
        path.Commands.Add(new ShapeLineToCommand(new ShapeAdjustPoint("r", "b")));
        path.Commands.Add(new ShapeLineToCommand(new ShapeAdjustPoint("l", "b")));
        path.Commands.Add(new ShapeClosePathCommand());
        geometry.Paths.Add(path);
        geometry.TextRectangle = new ShapeTextRectangle("l", "t", "r", "b");
        return geometry;
    }
}

public sealed class ShapeTextRectangle
{
    public string Left { get; set; }
    public string Top { get; set; }
    public string Right { get; set; }
    public string Bottom { get; set; }

    public ShapeTextRectangle(string left, string top, string right, string bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public ShapeTextRectangle Clone()
    {
        return new ShapeTextRectangle(Left, Top, Right, Bottom);
    }
}

public enum ShapePathFillMode
{
    None,
    Normal,
    Lighten,
    LightenLess,
    Darken,
    DarkenLess
}

public enum ShapePathFillRule
{
    NonZero,
    EvenOdd
}

public sealed class ShapePath
{
    public long Width { get; set; } = -1;
    public long Height { get; set; } = -1;
    public ShapePathFillMode FillMode { get; set; } = ShapePathFillMode.Normal;
    public ShapePathFillRule FillRule { get; set; } = ShapePathFillRule.NonZero;
    public bool IsStroked { get; set; } = true;
    public bool IsExtrusionOk { get; set; }
    public List<ShapePathCommand> Commands { get; } = new List<ShapePathCommand>();

    public bool IsFilled => FillMode != ShapePathFillMode.None;

    public ShapePath Clone()
    {
        var clone = new ShapePath
        {
            Width = Width,
            Height = Height,
            FillMode = FillMode,
            FillRule = FillRule,
            IsStroked = IsStroked,
            IsExtrusionOk = IsExtrusionOk
        };

        foreach (var command in Commands)
        {
            clone.Commands.Add(command.Clone());
        }

        return clone;
    }
}

public sealed class ShapeAdjustPoint
{
    public string X { get; }
    public string Y { get; }

    public ShapeAdjustPoint(string x, string y)
    {
        X = x;
        Y = y;
    }

    public ShapeAdjustPoint Clone() => new ShapeAdjustPoint(X, Y);
}

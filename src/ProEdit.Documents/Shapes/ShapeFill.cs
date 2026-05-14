using ProEdit.Primitives;

namespace ProEdit.Documents;

public enum ShapeFillKind
{
    None,
    Solid,
    Gradient,
    Pattern,
    Image
}

public abstract class ShapeFill
{
    public abstract ShapeFillKind Kind { get; }

    public abstract ShapeFill Clone();
}

public sealed class ShapeNoFill : ShapeFill
{
    public override ShapeFillKind Kind => ShapeFillKind.None;

    public override ShapeFill Clone() => new ShapeNoFill();
}

public sealed class ShapeSolidFill : ShapeFill
{
    public DocColor Color { get; set; }

    public ShapeSolidFill(DocColor color)
    {
        Color = color;
    }

    public override ShapeFillKind Kind => ShapeFillKind.Solid;

    public override ShapeFill Clone() => new ShapeSolidFill(Color);
}

public enum ShapeGradientType
{
    Linear,
    Radial
}

public sealed class ShapeGradientStop
{
    public float Position { get; set; }
    public DocColor Color { get; set; }

    public ShapeGradientStop(float position, DocColor color)
    {
        Position = position;
        Color = color;
    }

    public ShapeGradientStop Clone() => new ShapeGradientStop(Position, Color);
}

public sealed class ShapeGradientRect
{
    public float Left { get; set; }
    public float Top { get; set; }
    public float Right { get; set; }
    public float Bottom { get; set; }

    public ShapeGradientRect Clone()
    {
        return new ShapeGradientRect
        {
            Left = Left,
            Top = Top,
            Right = Right,
            Bottom = Bottom
        };
    }
}

public sealed class ShapeGradientFill : ShapeFill
{
    public ShapeGradientType Type { get; set; }
    public float Angle { get; set; }
    public bool IsScaled { get; set; }
    public ShapeGradientRect? FillRect { get; set; }
    public ShapeGradientRect? TileRect { get; set; }
    public List<ShapeGradientStop> Stops { get; } = new List<ShapeGradientStop>();

    public override ShapeFillKind Kind => ShapeFillKind.Gradient;

    public override ShapeFill Clone()
    {
        var clone = new ShapeGradientFill
        {
            Type = Type,
            Angle = Angle,
            IsScaled = IsScaled,
            FillRect = FillRect?.Clone(),
            TileRect = TileRect?.Clone()
        };

        foreach (var stop in Stops)
        {
            clone.Stops.Add(stop.Clone());
        }

        return clone;
    }
}

public sealed class ShapePatternFill : ShapeFill
{
    public string Pattern { get; set; } = string.Empty;
    public DocColor Foreground { get; set; } = DocColor.Black;
    public DocColor Background { get; set; } = DocColor.White;

    public override ShapeFillKind Kind => ShapeFillKind.Pattern;

    public override ShapeFill Clone()
    {
        return new ShapePatternFill
        {
            Pattern = Pattern,
            Foreground = Foreground,
            Background = Background
        };
    }
}

public enum ShapeImageFillMode
{
    Stretch,
    Tile
}

public sealed class ShapeImageTile
{
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;

    public ShapeImageTile Clone()
    {
        return new ShapeImageTile
        {
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            ScaleX = ScaleX,
            ScaleY = ScaleY
        };
    }
}

public sealed class ShapeImageFill : ShapeFill
{
    public byte[] Data { get; }
    public string ContentType { get; }
    public ImageCrop? Crop { get; set; }
    public ShapeImageFillMode Mode { get; set; } = ShapeImageFillMode.Stretch;
    public ShapeImageTile? Tile { get; set; }

    public ShapeImageFill(byte[] data, string contentType)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
    }

    public override ShapeFillKind Kind => ShapeFillKind.Image;

    public override ShapeFill Clone()
    {
        return new ShapeImageFill((byte[])Data.Clone(), ContentType)
        {
            Crop = Crop?.Clone(),
            Mode = Mode,
            Tile = Tile?.Clone()
        };
    }
}

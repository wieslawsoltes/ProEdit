using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public sealed class DrawingEffects : IEquatable<DrawingEffects>
{
    public DrawingShadowEffect? Shadow { get; set; }
    public DrawingGlowEffect? Glow { get; set; }
    public DrawingReflectionEffect? Reflection { get; set; }
    public DrawingSoftEdgeEffect? SoftEdge { get; set; }

    public bool HasValues =>
        Shadow is not null
        || Glow is not null
        || Reflection is not null
        || SoftEdge is not null;

    public DrawingEffects Clone()
    {
        return new DrawingEffects
        {
            Shadow = Shadow?.Clone(),
            Glow = Glow?.Clone(),
            Reflection = Reflection?.Clone(),
            SoftEdge = SoftEdge?.Clone()
        };
    }

    public bool Equals(DrawingEffects? other)
    {
        if (other is null)
        {
            return !HasValues;
        }

        return Equals(Shadow, other.Shadow)
               && Equals(Glow, other.Glow)
               && Equals(Reflection, other.Reflection)
               && Equals(SoftEdge, other.SoftEdge);
    }

    public override bool Equals(object? obj) => obj is DrawingEffects other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Shadow, Glow, Reflection, SoftEdge);
    }
}

public sealed class DrawingShadowEffect : IEquatable<DrawingShadowEffect>
{
    public DocColor Color { get; set; } = DocColor.Black;
    public float BlurRadius { get; set; }
    public float Distance { get; set; }
    public float Direction { get; set; }

    public DrawingShadowEffect Clone()
    {
        return new DrawingShadowEffect
        {
            Color = Color,
            BlurRadius = BlurRadius,
            Distance = Distance,
            Direction = Direction
        };
    }

    public bool Equals(DrawingShadowEffect? other)
    {
        if (other is null)
        {
            return false;
        }

        return Color.Equals(other.Color)
               && BlurRadius.Equals(other.BlurRadius)
               && Distance.Equals(other.Distance)
               && Direction.Equals(other.Direction);
    }

    public override bool Equals(object? obj) => obj is DrawingShadowEffect other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Color, BlurRadius, Distance, Direction);
    }
}

public sealed class DrawingGlowEffect : IEquatable<DrawingGlowEffect>
{
    public DocColor Color { get; set; } = DocColor.Black;
    public float Radius { get; set; }

    public DrawingGlowEffect Clone()
    {
        return new DrawingGlowEffect
        {
            Color = Color,
            Radius = Radius
        };
    }

    public bool Equals(DrawingGlowEffect? other)
    {
        if (other is null)
        {
            return false;
        }

        return Color.Equals(other.Color) && Radius.Equals(other.Radius);
    }

    public override bool Equals(object? obj) => obj is DrawingGlowEffect other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Color, Radius);
    }
}

public sealed class DrawingReflectionEffect : IEquatable<DrawingReflectionEffect>
{
    public float BlurRadius { get; set; }
    public float Distance { get; set; }
    public float StartOpacity { get; set; } = 0.35f;
    public float EndOpacity { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;

    public DrawingReflectionEffect Clone()
    {
        return new DrawingReflectionEffect
        {
            BlurRadius = BlurRadius,
            Distance = Distance,
            StartOpacity = StartOpacity,
            EndOpacity = EndOpacity,
            ScaleX = ScaleX,
            ScaleY = ScaleY
        };
    }

    public bool Equals(DrawingReflectionEffect? other)
    {
        if (other is null)
        {
            return false;
        }

        return BlurRadius.Equals(other.BlurRadius)
               && Distance.Equals(other.Distance)
               && StartOpacity.Equals(other.StartOpacity)
               && EndOpacity.Equals(other.EndOpacity)
               && ScaleX.Equals(other.ScaleX)
               && ScaleY.Equals(other.ScaleY);
    }

    public override bool Equals(object? obj) => obj is DrawingReflectionEffect other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(BlurRadius, Distance, StartOpacity, EndOpacity, ScaleX, ScaleY);
    }
}

public sealed class DrawingSoftEdgeEffect : IEquatable<DrawingSoftEdgeEffect>
{
    public float Radius { get; set; }

    public DrawingSoftEdgeEffect Clone()
    {
        return new DrawingSoftEdgeEffect
        {
            Radius = Radius
        };
    }

    public bool Equals(DrawingSoftEdgeEffect? other)
    {
        if (other is null)
        {
            return false;
        }

        return Radius.Equals(other.Radius);
    }

    public override bool Equals(object? obj) => obj is DrawingSoftEdgeEffect other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Radius);
    }
}

using ProEdit.Primitives;

namespace ProEdit.Documents;

public sealed class TextEffects : IEquatable<TextEffects>
{
    public TextOutlineEffect? Outline { get; set; }
    public TextShadowEffect? Shadow { get; set; }
    public bool? Emboss { get; set; }
    public bool? Imprint { get; set; }

    public bool HasValues =>
        Outline is not null
        || Shadow is not null
        || Emboss.HasValue
        || Imprint.HasValue;

    public TextEffects Clone()
    {
        return new TextEffects
        {
            Outline = Outline?.Clone(),
            Shadow = Shadow?.Clone(),
            Emboss = Emboss,
            Imprint = Imprint
        };
    }

    public void ApplyOverrides(TextEffects overrides)
    {
        if (overrides.Outline is not null)
        {
            Outline = overrides.Outline.Clone();
        }

        if (overrides.Shadow is not null)
        {
            Shadow = overrides.Shadow.Clone();
        }

        if (overrides.Emboss.HasValue)
        {
            Emboss = overrides.Emboss;
        }

        if (overrides.Imprint.HasValue)
        {
            Imprint = overrides.Imprint;
        }
    }

    public bool Equals(TextEffects? other)
    {
        if (other is null)
        {
            return !HasValues;
        }

        return Equals(Outline, other.Outline)
               && Equals(Shadow, other.Shadow)
               && Emboss == other.Emboss
               && Imprint == other.Imprint;
    }

    public override bool Equals(object? obj) => obj is TextEffects other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Outline, Shadow, Emboss, Imprint);
    }
}

public sealed class TextOutlineEffect : IEquatable<TextOutlineEffect>
{
    public bool Enabled { get; set; } = true;
    public DocColor? Color { get; set; }
    public float? Thickness { get; set; }

    public TextOutlineEffect Clone()
    {
        return new TextOutlineEffect
        {
            Enabled = Enabled,
            Color = Color,
            Thickness = Thickness
        };
    }

    public bool Equals(TextOutlineEffect? other)
    {
        if (other is null)
        {
            return false;
        }

        return Enabled == other.Enabled
               && Color.Equals(other.Color)
               && Thickness.Equals(other.Thickness);
    }

    public override bool Equals(object? obj) => obj is TextOutlineEffect other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Enabled, Color, Thickness);
    }
}

public sealed class TextShadowEffect : IEquatable<TextShadowEffect>
{
    public bool Enabled { get; set; } = true;
    public DocColor Color { get; set; } = DocColor.Black;
    public float BlurRadius { get; set; }
    public float Distance { get; set; }
    public float Direction { get; set; }

    public TextShadowEffect Clone()
    {
        return new TextShadowEffect
        {
            Enabled = Enabled,
            Color = Color,
            BlurRadius = BlurRadius,
            Distance = Distance,
            Direction = Direction
        };
    }

    public bool Equals(TextShadowEffect? other)
    {
        if (other is null)
        {
            return false;
        }

        return Enabled == other.Enabled
               && Color.Equals(other.Color)
               && BlurRadius.Equals(other.BlurRadius)
               && Distance.Equals(other.Distance)
               && Direction.Equals(other.Direction);
    }

    public override bool Equals(object? obj) => obj is TextShadowEffect other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Enabled, Color, BlurRadius, Distance, Direction);
    }
}

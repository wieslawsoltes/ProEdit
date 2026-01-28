namespace Vibe.Office.Documents;

[Flags]
public enum DocLigatureOptions
{
    None = 0,
    Standard = 1 << 0,
    Contextual = 1 << 1,
    Discretional = 1 << 2,
    Historical = 1 << 3
}

public enum DocNumberForm
{
    Default,
    Lining,
    OldStyle
}

public enum DocNumberSpacing
{
    Default,
    Proportional,
    Tabular
}

public sealed class TextOpenTypeFeatures : IEquatable<TextOpenTypeFeatures>
{
    public DocLigatureOptions? Ligatures { get; set; }
    public bool? ContextualAlternates { get; set; }
    public DocNumberForm? NumberForm { get; set; }
    public DocNumberSpacing? NumberSpacing { get; set; }
    public uint? StylisticSets { get; set; }

    public bool HasValues =>
        Ligatures.HasValue
        || ContextualAlternates.HasValue
        || NumberForm.HasValue
        || NumberSpacing.HasValue
        || StylisticSets.HasValue;

    public TextOpenTypeFeatures Clone()
    {
        return new TextOpenTypeFeatures
        {
            Ligatures = Ligatures,
            ContextualAlternates = ContextualAlternates,
            NumberForm = NumberForm,
            NumberSpacing = NumberSpacing,
            StylisticSets = StylisticSets
        };
    }

    public void ApplyOverrides(TextOpenTypeFeatures overrides)
    {
        if (overrides.Ligatures.HasValue)
        {
            Ligatures = overrides.Ligatures;
        }

        if (overrides.ContextualAlternates.HasValue)
        {
            ContextualAlternates = overrides.ContextualAlternates;
        }

        if (overrides.NumberForm.HasValue)
        {
            NumberForm = overrides.NumberForm;
        }

        if (overrides.NumberSpacing.HasValue)
        {
            NumberSpacing = overrides.NumberSpacing;
        }

        if (overrides.StylisticSets.HasValue)
        {
            StylisticSets = overrides.StylisticSets;
        }
    }

    public bool Equals(TextOpenTypeFeatures? other)
    {
        if (other is null)
        {
            return !HasValues;
        }

        return Ligatures == other.Ligatures
               && ContextualAlternates == other.ContextualAlternates
               && NumberForm == other.NumberForm
               && NumberSpacing == other.NumberSpacing
               && StylisticSets == other.StylisticSets;
    }

    public override bool Equals(object? obj) => obj is TextOpenTypeFeatures other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Ligatures, ContextualAlternates, NumberForm, NumberSpacing, StylisticSets);
    }
}

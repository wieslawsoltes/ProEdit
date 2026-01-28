namespace Vibe.Office.Documents;

public sealed class DocumentCompatibilitySettings : IEquatable<DocumentCompatibilitySettings>
{
    public bool? SuppressSpacingAtTopOfPage { get; set; }
    public bool? SuppressSpacingBeforeAfterPageBreak { get; set; }
    public bool? UseWord97LineBreakRules { get; set; }
    public bool? WrapTrailSpaces { get; set; }
    public bool? DoNotUseEastAsianBreakRules { get; set; }
    public bool? UseAltKinsokuLineBreakRules { get; set; }
    public bool? DoNotWrapTextWithPunctuation { get; set; }

    public bool HasValues =>
        SuppressSpacingAtTopOfPage.HasValue
        || SuppressSpacingBeforeAfterPageBreak.HasValue
        || UseWord97LineBreakRules.HasValue
        || WrapTrailSpaces.HasValue
        || DoNotUseEastAsianBreakRules.HasValue
        || UseAltKinsokuLineBreakRules.HasValue
        || DoNotWrapTextWithPunctuation.HasValue;

    public DocumentCompatibilitySettings Clone()
    {
        return new DocumentCompatibilitySettings
        {
            SuppressSpacingAtTopOfPage = SuppressSpacingAtTopOfPage,
            SuppressSpacingBeforeAfterPageBreak = SuppressSpacingBeforeAfterPageBreak,
            UseWord97LineBreakRules = UseWord97LineBreakRules,
            WrapTrailSpaces = WrapTrailSpaces,
            DoNotUseEastAsianBreakRules = DoNotUseEastAsianBreakRules,
            UseAltKinsokuLineBreakRules = UseAltKinsokuLineBreakRules,
            DoNotWrapTextWithPunctuation = DoNotWrapTextWithPunctuation
        };
    }

    public bool Equals(DocumentCompatibilitySettings? other)
    {
        if (other is null)
        {
            return false;
        }

        return SuppressSpacingAtTopOfPage == other.SuppressSpacingAtTopOfPage
               && SuppressSpacingBeforeAfterPageBreak == other.SuppressSpacingBeforeAfterPageBreak
               && UseWord97LineBreakRules == other.UseWord97LineBreakRules
               && WrapTrailSpaces == other.WrapTrailSpaces
               && DoNotUseEastAsianBreakRules == other.DoNotUseEastAsianBreakRules
               && UseAltKinsokuLineBreakRules == other.UseAltKinsokuLineBreakRules
               && DoNotWrapTextWithPunctuation == other.DoNotWrapTextWithPunctuation;
    }

    public override bool Equals(object? obj) => obj is DocumentCompatibilitySettings other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            SuppressSpacingAtTopOfPage,
            SuppressSpacingBeforeAfterPageBreak,
            UseWord97LineBreakRules,
            WrapTrailSpaces,
            DoNotUseEastAsianBreakRules,
            UseAltKinsokuLineBreakRules,
            DoNotWrapTextWithPunctuation);
    }
}

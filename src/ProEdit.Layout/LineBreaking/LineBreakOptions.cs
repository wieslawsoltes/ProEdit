using ProEdit.Documents;

namespace ProEdit.Layout;

internal readonly struct LineBreakOptions
{
    public LineBreakOptions(DocumentCompatibilitySettings compatibility)
    {
        UseWord97LineBreakRules = compatibility.UseWord97LineBreakRules == true;
        WrapTrailingSpaces = compatibility.WrapTrailSpaces == true;
        UseEastAsianBreakRules = compatibility.DoNotUseEastAsianBreakRules != true;
        UseAltKinsokuLineBreakRules = compatibility.UseAltKinsokuLineBreakRules == true;
        DoNotWrapTextWithPunctuation = compatibility.DoNotWrapTextWithPunctuation == true;
    }

    public bool UseWord97LineBreakRules { get; }
    public bool WrapTrailingSpaces { get; }
    public bool UseEastAsianBreakRules { get; }
    public bool UseAltKinsokuLineBreakRules { get; }
    public bool DoNotWrapTextWithPunctuation { get; }
}

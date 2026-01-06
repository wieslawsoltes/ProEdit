namespace Vibe.Office.Documents;

public sealed class ParagraphStyleProperties
{
    public ParagraphAlignment? Alignment { get; set; }
    public float? SpacingBefore { get; set; }
    public float? SpacingAfter { get; set; }
    public int? LineSpacing { get; set; }
    public DocLineSpacingRule? LineSpacingRule { get; set; }
    public float? IndentLeft { get; set; }
    public float? IndentRight { get; set; }
    public float? FirstLineIndent { get; set; }
    public List<float> TabStops { get; } = new List<float>();
    public bool? KeepWithNext { get; set; }
    public bool? KeepLinesTogether { get; set; }
    public bool? WidowControl { get; set; }
    public bool? PageBreakBefore { get; set; }
    public bool? ContextualSpacing { get; set; }
    public bool? Bidi { get; set; }
    public Vibe.Office.Primitives.DocColor? ShadingColor { get; set; }
    public ParagraphBorders Borders { get; } = new ParagraphBorders();

    public bool HasValues => Alignment.HasValue
                             || SpacingBefore.HasValue
                             || SpacingAfter.HasValue
                             || LineSpacing.HasValue
                             || LineSpacingRule.HasValue
                             || IndentLeft.HasValue
                             || IndentRight.HasValue
                             || FirstLineIndent.HasValue
                             || TabStops.Count > 0
                             || KeepWithNext.HasValue
                             || KeepLinesTogether.HasValue
                             || WidowControl.HasValue
                             || PageBreakBefore.HasValue
                             || ContextualSpacing.HasValue
                             || Bidi.HasValue
                             || ShadingColor.HasValue
                             || Borders.HasAny;
}

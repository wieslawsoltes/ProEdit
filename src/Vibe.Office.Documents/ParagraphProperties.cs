using Vibe.Office.Primitives;

namespace Vibe.Office.Documents;

public enum ParagraphAlignment
{
    Left,
    Center,
    Right,
    Justify
}

public sealed class ParagraphProperties
{
    public ParagraphAlignment? Alignment { get; set; }
    public float? SpacingBefore { get; set; }
    public float? SpacingAfter { get; set; }
    public int? LineSpacing { get; set; }
    public DocLineSpacingRule? LineSpacingRule { get; set; }
    public float? IndentLeft { get; set; }
    public float? IndentRight { get; set; }
    public float? FirstLineIndent { get; set; }
    public List<TabStopDefinition> TabStops { get; } = new List<TabStopDefinition>();
    public bool? KeepWithNext { get; set; }
    public bool? KeepLinesTogether { get; set; }
    public bool? WidowControl { get; set; }
    public bool? PageBreakBefore { get; set; }
    public bool? ContextualSpacing { get; set; }
    public bool? Bidi { get; set; }
    public DocColor? ShadingColor { get; set; }
    public ParagraphBorders Borders { get; } = new ParagraphBorders();
}

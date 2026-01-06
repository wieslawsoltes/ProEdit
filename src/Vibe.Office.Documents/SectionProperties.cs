namespace Vibe.Office.Documents;

public sealed class SectionProperties
{
    public float? PageWidth { get; set; }
    public float? PageHeight { get; set; }
    public float? MarginLeft { get; set; }
    public float? MarginTop { get; set; }
    public float? MarginRight { get; set; }
    public float? MarginBottom { get; set; }
    public float? HeaderOffset { get; set; }
    public float? FooterOffset { get; set; }
    public int? ColumnCount { get; set; }
    public float? ColumnGap { get; set; }
    public bool? ColumnEqualWidth { get; set; }
    public List<float> ColumnWidths { get; } = new List<float>();

    public bool HasValues =>
        PageWidth.HasValue
        || PageHeight.HasValue
        || MarginLeft.HasValue
        || MarginTop.HasValue
        || MarginRight.HasValue
        || MarginBottom.HasValue
        || HeaderOffset.HasValue
        || FooterOffset.HasValue
        || ColumnCount.HasValue
        || ColumnGap.HasValue
        || ColumnEqualWidth.HasValue
        || ColumnWidths.Count > 0;
}

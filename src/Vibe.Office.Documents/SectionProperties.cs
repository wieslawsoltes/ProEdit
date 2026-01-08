namespace Vibe.Office.Documents;

public sealed class SectionProperties
{
    public float? PageWidth { get; set; }
    public float? PageHeight { get; set; }
    public PageOrientation? Orientation { get; set; }
    public float? MarginLeft { get; set; }
    public float? MarginTop { get; set; }
    public float? MarginRight { get; set; }
    public float? MarginBottom { get; set; }
    public float? HeaderOffset { get; set; }
    public float? FooterOffset { get; set; }
    public float? Gutter { get; set; }
    public bool? DifferentFirstPageHeaderFooter { get; set; }
    public int? ColumnCount { get; set; }
    public float? ColumnGap { get; set; }
    public bool? ColumnEqualWidth { get; set; }
    public bool? ColumnSeparator { get; set; }
    public List<float> ColumnWidths { get; } = new List<float>();
    public DocGridSettings? DocGrid { get; set; }

    public bool HasValues =>
        PageWidth.HasValue
        || PageHeight.HasValue
        || Orientation.HasValue
        || MarginLeft.HasValue
        || MarginTop.HasValue
        || MarginRight.HasValue
        || MarginBottom.HasValue
        || HeaderOffset.HasValue
        || FooterOffset.HasValue
        || Gutter.HasValue
        || DifferentFirstPageHeaderFooter.HasValue
        || ColumnCount.HasValue
        || ColumnGap.HasValue
        || ColumnEqualWidth.HasValue
        || ColumnSeparator.HasValue
        || ColumnWidths.Count > 0
        || (DocGrid?.HasValues ?? false);

    public SectionProperties Clone()
    {
        var clone = new SectionProperties
        {
            PageWidth = PageWidth,
            PageHeight = PageHeight,
            Orientation = Orientation,
            MarginLeft = MarginLeft,
            MarginTop = MarginTop,
            MarginRight = MarginRight,
            MarginBottom = MarginBottom,
            HeaderOffset = HeaderOffset,
            FooterOffset = FooterOffset,
            Gutter = Gutter,
            DifferentFirstPageHeaderFooter = DifferentFirstPageHeaderFooter,
            ColumnCount = ColumnCount,
            ColumnGap = ColumnGap,
            ColumnEqualWidth = ColumnEqualWidth,
            ColumnSeparator = ColumnSeparator,
            DocGrid = DocGrid?.Clone()
        };

        if (ColumnWidths.Count > 0)
        {
            clone.ColumnWidths.AddRange(ColumnWidths);
        }

        return clone;
    }
}

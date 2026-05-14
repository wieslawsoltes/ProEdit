using ProEdit.Primitives;

namespace ProEdit.Documents;

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
    public List<float> ColumnGaps { get; } = new List<float>();
    public DocGridSettings? DocGrid { get; set; }
    public DocColor? PageBackgroundColor { get; set; }
    public PageBorders? PageBorders { get; set; }
    public LineNumberingSettings? LineNumbering { get; set; }
    public PageNumberingSettings? PageNumbering { get; set; }
    public FootnoteSettings? Footnotes { get; set; }
    public EndnoteSettings? Endnotes { get; set; }

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
        || ColumnGaps.Count > 0
        || (DocGrid?.HasValues ?? false)
        || PageBackgroundColor.HasValue
        || (PageBorders?.HasAny ?? false)
        || (LineNumbering?.HasValues ?? false)
        || (PageNumbering?.HasValues ?? false)
        || (Footnotes?.HasValues ?? false)
        || (Endnotes?.HasValues ?? false);

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
            DocGrid = DocGrid?.Clone(),
            LineNumbering = LineNumbering?.Clone(),
            PageNumbering = PageNumbering?.Clone(),
            Footnotes = Footnotes?.Clone(),
            Endnotes = Endnotes?.Clone()
        };

        if (ColumnWidths.Count > 0)
        {
            clone.ColumnWidths.AddRange(ColumnWidths);
        }

        if (ColumnGaps.Count > 0)
        {
            clone.ColumnGaps.AddRange(ColumnGaps);
        }

        if (PageBackgroundColor.HasValue)
        {
            clone.PageBackgroundColor = PageBackgroundColor.Value;
        }

        if (PageBorders is not null)
        {
            clone.PageBorders = PageBorders.Clone();
        }

        if (LineNumbering is not null)
        {
            clone.LineNumbering = LineNumbering.Clone();
        }

        if (PageNumbering is not null)
        {
            clone.PageNumbering = PageNumbering.Clone();
        }

        if (Footnotes is not null)
        {
            clone.Footnotes = Footnotes.Clone();
        }

        if (Endnotes is not null)
        {
            clone.Endnotes = Endnotes.Clone();
        }

        return clone;
    }
}

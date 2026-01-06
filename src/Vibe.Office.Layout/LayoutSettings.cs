namespace Vibe.Office.Layout;

public sealed class LayoutSettings
{
    public float ViewportWidth { get; set; } = 800f;
    public float ViewportHeight { get; set; } = 600f;
    public bool UsePagination { get; set; } = true;
    public float PageWidth { get; set; } = 816f;
    public float PageHeight { get; set; } = 1056f;
    public float PageGap { get; set; } = 24f;
    public float MarginLeft { get; set; } = 24f;
    public float MarginTop { get; set; } = 24f;
    public float MarginRight { get; set; } = 24f;
    public float MarginBottom { get; set; } = 24f;
    public float HeaderOffset { get; set; } = 12f;
    public float FooterOffset { get; set; } = 12f;
    public float ParagraphSpacing { get; set; } = 6f;
    public float BlockSpacing { get; set; } = 12f;
    public float ListIndent { get; set; } = 24f;
    public float ListMarkerGap { get; set; } = 6f;
    public float DefaultTabWidth { get; set; } = 48f;
    public float ColumnGap { get; set; } = 48f;
    public float TableCellPadding { get; set; } = 6f;
    public float TableBorderThickness { get; set; } = 1f;

    public float ContentWidth => MathF.Max(0f, (UsePagination ? PageWidth : ViewportWidth) - MarginLeft - MarginRight);
    public float PageContentHeight => MathF.Max(0f, PageHeight - MarginTop - MarginBottom);

    public LayoutSettings Clone()
    {
        return new LayoutSettings
        {
            ViewportWidth = ViewportWidth,
            ViewportHeight = ViewportHeight,
            UsePagination = UsePagination,
            PageWidth = PageWidth,
            PageHeight = PageHeight,
            PageGap = PageGap,
            MarginLeft = MarginLeft,
            MarginTop = MarginTop,
            MarginRight = MarginRight,
            MarginBottom = MarginBottom,
            HeaderOffset = HeaderOffset,
            FooterOffset = FooterOffset,
            ParagraphSpacing = ParagraphSpacing,
            BlockSpacing = BlockSpacing,
            ListIndent = ListIndent,
            ListMarkerGap = ListMarkerGap,
            DefaultTabWidth = DefaultTabWidth,
            ColumnGap = ColumnGap,
            TableCellPadding = TableCellPadding,
            TableBorderThickness = TableBorderThickness
        };
    }
}

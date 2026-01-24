namespace Vibe.Office.Layout;

public enum PageFlowDirection
{
    Vertical,
    Horizontal
}

public sealed class LayoutSettings
{
    public float ViewportWidth { get; set; } = 800f;
    public float ViewportHeight { get; set; } = 600f;
    public bool UsePagination { get; set; } = true;
    public float PageWidth { get; set; } = 816f;
    public float PageHeight { get; set; } = 1056f;
    public float PageGap { get; set; } = 24f;
    public PageFlowDirection PageFlow { get; set; } = PageFlowDirection.Vertical;
    public float MarginLeft { get; set; } = 96f;
    public float MarginTop { get; set; } = 96f;
    public float MarginRight { get; set; } = 96f;
    public float MarginBottom { get; set; } = 96f;
    public float HeaderOffset { get; set; } = 48f;
    public float FooterOffset { get; set; } = 48f;
    public float Gutter { get; set; } = 0f;
    public float ParagraphSpacing { get; set; } = 0f;
    public float BlockSpacing { get; set; } = 0f;
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
            PageFlow = PageFlow,
            MarginLeft = MarginLeft,
            MarginTop = MarginTop,
            MarginRight = MarginRight,
            MarginBottom = MarginBottom,
            HeaderOffset = HeaderOffset,
            FooterOffset = FooterOffset,
            Gutter = Gutter,
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

namespace Vibe.Office.Layout;

public sealed record HeaderFooterLayout(
    int PageIndex,
    IReadOnlyList<HeaderFooterLine> HeaderLines,
    IReadOnlyList<HeaderFooterLine> FooterLines);

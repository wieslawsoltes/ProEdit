namespace Vibe.Office.Layout;

public sealed record HeaderFooterLayout(
    int PageIndex,
    IReadOnlyList<HeaderFooterLine> HeaderLines,
    IReadOnlyList<HeaderFooterLine> FooterLines,
    IReadOnlyList<TableLayout> HeaderTables,
    IReadOnlyList<TableLayout> FooterTables,
    IReadOnlyList<FloatingLayoutObject> FloatingObjects);

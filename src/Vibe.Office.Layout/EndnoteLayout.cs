using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public sealed record EndnoteLayout(
    int PageIndex,
    IReadOnlyList<HeaderFooterLine> Lines,
    IReadOnlyList<TableLayout> Tables,
    DocRect SeparatorBounds);

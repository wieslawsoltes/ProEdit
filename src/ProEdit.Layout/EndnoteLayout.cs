using ProEdit.Primitives;

namespace ProEdit.Layout;

public sealed record EndnoteLayout(
    int PageIndex,
    IReadOnlyList<HeaderFooterLine> Lines,
    IReadOnlyList<TableLayout> Tables,
    DocRect SeparatorBounds);

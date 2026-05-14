using ProEdit.Primitives;

namespace ProEdit.Layout;

public sealed record FootnoteLayout(
    int PageIndex,
    IReadOnlyList<HeaderFooterLine> Lines,
    IReadOnlyList<TableLayout> Tables,
    DocRect SeparatorBounds);

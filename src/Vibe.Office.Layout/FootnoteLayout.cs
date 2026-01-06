using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public sealed record FootnoteLayout(
    int PageIndex,
    IReadOnlyList<HeaderFooterLine> Lines,
    DocRect SeparatorBounds);

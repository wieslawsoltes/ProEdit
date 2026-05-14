using ProEdit.Documents;
using ProEdit.Primitives;

namespace ProEdit.Layout;

public sealed record FloatingLayoutObject(
    FloatingObject Object,
    int ParagraphIndex,
    int PageIndex,
    DocRect Bounds,
    FloatingWrapContour? WrapContour);

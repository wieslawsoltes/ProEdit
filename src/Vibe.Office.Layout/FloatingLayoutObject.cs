using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public sealed record FloatingLayoutObject(
    FloatingObject Object,
    int ParagraphIndex,
    int PageIndex,
    DocRect Bounds,
    FloatingWrapContour? WrapContour);

using ProEdit.Primitives;

namespace ProEdit.Layout;

public sealed record PageLayout(int Index, DocRect Bounds, DocRect ContentBounds);

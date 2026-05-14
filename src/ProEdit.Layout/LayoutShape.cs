using ProEdit.Documents;

namespace ProEdit.Layout;

public sealed record LayoutShape(ShapeInline Shape, float X, float Width, float Height, int Length);

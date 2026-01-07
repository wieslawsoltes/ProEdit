using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public sealed record LayoutShape(ShapeInline Shape, float X, float Width, float Height, int Length);

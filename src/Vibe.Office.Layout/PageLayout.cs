using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public sealed record PageLayout(int Index, DocRect Bounds, DocRect ContentBounds);

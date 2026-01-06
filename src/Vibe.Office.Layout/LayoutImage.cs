using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public sealed record LayoutImage(ImageInline Image, float X, float Width, float Height, int Length);

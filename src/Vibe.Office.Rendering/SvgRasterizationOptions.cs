using Vibe.Office.Primitives;

namespace Vibe.Office.Rendering;

public readonly record struct SvgRasterizationOptions(
    float Width,
    float Height,
    float Scale,
    DocColor BackgroundColor);

using ProEdit.Primitives;

namespace ProEdit.Rendering;

public readonly record struct SvgRasterizationOptions(
    float Width,
    float Height,
    float Scale,
    DocColor BackgroundColor);

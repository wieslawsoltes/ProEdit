namespace Vibe.Office.Layout;

internal sealed record LineLayout(
    IReadOnlyList<LayoutRun> Runs,
    IReadOnlyList<LayoutImage> Images,
    IReadOnlyList<LayoutShape> Shapes,
    IReadOnlyList<LayoutChart> Charts,
    IReadOnlyList<LayoutEquation> Equations,
    float Width,
    float LineHeight,
    float Ascent);

namespace Vibe.Office.Layout;

public sealed record TableCellLine(
    int ParagraphIndex,
    int StartOffset,
    int Length,
    float X,
    float Y,
    float Width,
    string Text,
    string? Prefix,
    float PrefixWidth,
    float LineHeight,
    float Ascent,
    IReadOnlyList<LayoutRun> Runs,
    IReadOnlyList<LayoutImage> Images,
    IReadOnlyList<LayoutShape> Shapes,
    IReadOnlyList<LayoutChart> Charts);

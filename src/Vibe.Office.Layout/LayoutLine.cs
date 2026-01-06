namespace Vibe.Office.Layout;

public sealed record LayoutLine(
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
    bool IsInTable = false);

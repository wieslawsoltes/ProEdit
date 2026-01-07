namespace Vibe.Office.Layout;

public sealed record LayoutLine(
    int ParagraphIndex,
    int StartOffset,
    int Length,
    float X,
    float Y,
    float Width,
    TextSlice TextSlice,
    string? Prefix,
    float PrefixWidth,
    float LineHeight,
    float Ascent,
    IReadOnlyList<LayoutRun> Runs,
    IReadOnlyList<LayoutImage> Images,
    IReadOnlyList<LayoutShape> Shapes,
    IReadOnlyList<LayoutChart> Charts,
    IReadOnlyList<LayoutEquation> Equations,
    bool IsInTable = false,
    bool IsRtl = false)
{
    private string? _text;

    public string Text => _text ??= TextSlice.ToString();

    public ReadOnlySpan<char> TextSpan => TextSlice.Span;
}

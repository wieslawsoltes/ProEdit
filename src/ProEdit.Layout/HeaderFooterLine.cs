using ProEdit.Documents;

namespace ProEdit.Layout;

public sealed record HeaderFooterLine(
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
    IReadOnlyList<LayoutRuby> Rubies,
    DocTextDirection? TextDirection,
    bool IsRtl = false)
{
    private string? _text;

    public string Text => _text ??= TextSlice.ToString();

    public ReadOnlySpan<char> TextSpan => TextSlice.Span;
}

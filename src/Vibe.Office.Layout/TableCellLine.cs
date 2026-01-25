using Vibe.Office.Documents;
using Vibe.Office.Primitives;

namespace Vibe.Office.Layout;

public sealed record TableCellLine(
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
    bool IsRtl = false,
    ParagraphBorders? ParagraphBorders = null,
    DocColor? ParagraphShadingColor = null,
    bool IsParagraphStart = false,
    bool IsParagraphEnd = false)
{
    private string? _text;

    public string Text => _text ??= TextSlice.ToString();

    public ReadOnlySpan<char> TextSpan => TextSlice.Span;
}

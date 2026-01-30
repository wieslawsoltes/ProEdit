using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

internal sealed record InlineSpan(
    int Start,
    int Length,
    string Text,
    TextStyle Style,
    ImageInline? Image,
    ShapeInline? Shape,
    ChartInline? Chart,
    EquationInline? Equation,
    RubyInline? Ruby,
    TextStyle? RubyStyle,
    float BaselineOffset)
{
    public ContentControlProperties? ContentControl { get; init; }
    public bool ContentControlIsPlaceholder { get; init; }
}

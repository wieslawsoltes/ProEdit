using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public sealed record LayoutRuby(
    int StartOffset,
    int Length,
    string RubyText,
    TextStyle RubyStyle,
    float BaselineOffset);

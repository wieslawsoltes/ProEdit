using ProEdit.Documents;

namespace ProEdit.Layout;

public sealed record LayoutRuby(
    int StartOffset,
    int Length,
    string RubyText,
    TextStyle RubyStyle,
    float BaselineOffset);

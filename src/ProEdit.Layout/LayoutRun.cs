using ProEdit.Documents;

namespace ProEdit.Layout;

public sealed record LayoutRun(string Text, TextStyle Style, float X, float Width, int Length, bool IsTab, float BaselineOffset, TabLeader TabLeader = TabLeader.None, float LetterSpacing = 0f)
{
    public ContentControlProperties? ContentControl { get; init; }
    public bool ContentControlIsPlaceholder { get; init; }
}

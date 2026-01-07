using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

internal readonly record struct ParagraphLineBreak(int Start, int Length, bool HasHyphen, TextStyle? HyphenStyle, float HyphenBaselineOffset)
{
    public string Text(string fullText)
    {
        return Length <= 0 ? string.Empty : fullText.Substring(Start, Length);
    }
}

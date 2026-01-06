namespace Vibe.Office.Documents;

public sealed class PageNumberInline : Inline
{
    public TextStyle? Style { get; set; }

    public PageNumberInline(TextStyle? style = null)
    {
        Style = style;
    }
}

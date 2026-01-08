namespace Vibe.Office.Documents;

public sealed class TotalPagesInline : Inline
{
    public TextStyle? Style { get; set; }

    public TotalPagesInline(TextStyle? style = null)
    {
        Style = style;
    }
}

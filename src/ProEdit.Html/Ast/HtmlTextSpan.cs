namespace ProEdit.Html.Ast;

public readonly record struct HtmlTextSpan(int Start, int Length)
{
    public static HtmlTextSpan Unknown { get; } = new(-1, 0);

    public int End => Start < 0 ? -1 : Start + Length;

    public bool IsKnown => Start >= 0;
}

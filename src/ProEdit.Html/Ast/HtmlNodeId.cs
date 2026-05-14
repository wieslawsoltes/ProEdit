namespace ProEdit.Html.Ast;

public readonly record struct HtmlNodeId(long Value)
{
    public static HtmlNodeId Empty { get; } = new(0);

    public bool IsEmpty => Value <= 0;
}

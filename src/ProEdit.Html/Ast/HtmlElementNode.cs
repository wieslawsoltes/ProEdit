namespace ProEdit.Html.Ast;

public sealed class HtmlElementNode : HtmlNode
{
    public HtmlElementNode(HtmlNodeId id, HtmlTextSpan span, string name)
        : base(id, span)
    {
        Name = name;
    }

    public string Name { get; set; }

    public List<HtmlAttribute> Attributes { get; } = new();

    public List<HtmlNode> Children { get; } = new();

    public bool IsVoidElement { get; set; }
}

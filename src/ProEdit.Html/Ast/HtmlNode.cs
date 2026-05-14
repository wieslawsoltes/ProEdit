namespace ProEdit.Html.Ast;

public abstract class HtmlNode
{
    protected HtmlNode(HtmlNodeId id, HtmlTextSpan span)
    {
        Id = id;
        Span = span;
    }

    public HtmlNodeId Id { get; internal set; }

    public HtmlTextSpan Span { get; set; }
}

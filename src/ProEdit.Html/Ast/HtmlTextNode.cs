namespace ProEdit.Html.Ast;

public sealed class HtmlTextNode : HtmlNode
{
    public HtmlTextNode(HtmlNodeId id, HtmlTextSpan span, string text)
        : base(id, span)
    {
        Text = text;
    }

    public string Text { get; set; }
}

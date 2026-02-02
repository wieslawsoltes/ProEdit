namespace Vibe.Office.Html.Ast;

public sealed class HtmlCommentNode : HtmlNode
{
    public HtmlCommentNode(HtmlNodeId id, HtmlTextSpan span, string text)
        : base(id, span)
    {
        Text = text;
    }

    public string Text { get; set; }
}

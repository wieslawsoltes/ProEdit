namespace Vibe.Office.Html.Ast;

public sealed class HtmlDocument : HtmlNode
{
    public HtmlDocument(HtmlNodeId id, HtmlTextSpan span)
        : base(id, span)
    {
    }

    public List<HtmlNode> Children { get; } = new();
}

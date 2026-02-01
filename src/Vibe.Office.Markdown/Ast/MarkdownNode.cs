namespace Vibe.Office.Markdown.Ast;

public abstract class MarkdownNode
{
    protected MarkdownNode(MarkdownNodeId id, MarkdownTextSpan span)
    {
        Id = id;
        Span = span;
    }

    public MarkdownNodeId Id { get; internal set; }

    public MarkdownTextSpan Span { get; set; }
}

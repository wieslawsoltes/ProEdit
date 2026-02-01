namespace Vibe.Office.Markdown.Ast;

public abstract class MarkdownInline : MarkdownNode
{
    protected MarkdownInline(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }
}

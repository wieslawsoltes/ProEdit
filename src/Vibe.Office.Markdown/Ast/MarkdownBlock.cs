namespace Vibe.Office.Markdown.Ast;

public abstract class MarkdownBlock : MarkdownNode
{
    protected MarkdownBlock(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }
}

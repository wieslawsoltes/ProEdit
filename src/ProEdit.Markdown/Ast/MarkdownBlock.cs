namespace ProEdit.Markdown.Ast;

public abstract class MarkdownBlock : MarkdownNode
{
    protected MarkdownBlock(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }
}

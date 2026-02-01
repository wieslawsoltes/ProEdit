namespace Vibe.Office.Markdown.Ast;

public sealed class MarkdownDocument : MarkdownNode
{
    public MarkdownDocument(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public List<MarkdownBlock> Blocks { get; } = new();
}

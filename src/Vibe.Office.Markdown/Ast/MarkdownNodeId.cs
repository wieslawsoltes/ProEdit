namespace Vibe.Office.Markdown.Ast;

public readonly record struct MarkdownNodeId(long Value)
{
    public static MarkdownNodeId Empty { get; } = new(0);

    public bool IsEmpty => Value <= 0;
}

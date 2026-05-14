namespace ProEdit.Markdown.Ast;

public readonly record struct MarkdownTextSpan(int Start, int Length)
{
    public static MarkdownTextSpan Unknown { get; } = new(-1, 0);

    public int End => Start < 0 ? -1 : Start + Length;

    public bool IsKnown => Start >= 0;
}

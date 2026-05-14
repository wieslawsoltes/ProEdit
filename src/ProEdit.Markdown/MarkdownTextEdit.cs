namespace ProEdit.Markdown;

public readonly record struct MarkdownTextEdit(int Start, int Length, string NewText);

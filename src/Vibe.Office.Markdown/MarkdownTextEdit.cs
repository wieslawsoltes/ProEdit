namespace Vibe.Office.Markdown;

public readonly record struct MarkdownTextEdit(int Start, int Length, string NewText);

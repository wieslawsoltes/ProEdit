namespace Vibe.Office.Markdown;

public readonly record struct MarkdownTextUpdate(string Text, IReadOnlyList<MarkdownTextEdit> Edits);

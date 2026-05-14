namespace ProEdit.Markdown;

public readonly record struct MarkdownTextUpdate(string Text, IReadOnlyList<MarkdownTextEdit> Edits);

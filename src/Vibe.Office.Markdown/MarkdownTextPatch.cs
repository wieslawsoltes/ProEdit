namespace Vibe.Office.Markdown;

public static class MarkdownTextPatch
{
    public static string Apply(string text, IReadOnlyList<MarkdownTextEdit> edits)
    {
        text ??= string.Empty;
        if (edits is null || edits.Count == 0)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var position = 0;
        foreach (var edit in edits.OrderBy(e => e.Start))
        {
            if (edit.Start < position || edit.Start > text.Length)
            {
                continue;
            }

            if (edit.Start > position)
            {
                builder.Append(text.AsSpan(position, edit.Start - position));
            }

            builder.Append(edit.NewText);
            position = Math.Clamp(edit.Start + edit.Length, position, text.Length);
        }

        if (position < text.Length)
        {
            builder.Append(text.AsSpan(position));
        }

        return builder.ToString();
    }
}

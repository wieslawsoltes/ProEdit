namespace Vibe.Office.Markdown;

public static class MarkdownTextDiff
{
    public static IReadOnlyList<MarkdownTextEdit> ComputeSingleEdit(string oldText, string newText)
    {
        oldText ??= string.Empty;
        newText ??= string.Empty;

        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            return Array.Empty<MarkdownTextEdit>();
        }

        var prefix = 0;
        var minLength = Math.Min(oldText.Length, newText.Length);
        while (prefix < minLength && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }

        var oldSuffix = oldText.Length;
        var newSuffix = newText.Length;
        while (oldSuffix > prefix && newSuffix > prefix && oldText[oldSuffix - 1] == newText[newSuffix - 1])
        {
            oldSuffix--;
            newSuffix--;
        }

        var deleteLength = Math.Max(0, oldSuffix - prefix);
        var insertText = newText.Substring(prefix, newSuffix - prefix);
        return new[] { new MarkdownTextEdit(prefix, deleteLength, insertText) };
    }
}

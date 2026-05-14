namespace ProEdit.Html;

public static class HtmlTextPatch
{
    public static string Apply(string text, HtmlTextEdit edit)
    {
        text ??= string.Empty;
        var start = Math.Clamp(edit.Start, 0, text.Length);
        var length = Math.Clamp(edit.Length, 0, text.Length - start);
        var insert = edit.NewText ?? string.Empty;

        if (length == 0 && insert.Length == 0)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, start), insert, text.AsSpan(start + length));
    }

    public static string ApplyAll(string text, IReadOnlyList<HtmlTextEdit> edits)
    {
        text ??= string.Empty;
        if (edits is null || edits.Count == 0)
        {
            return text;
        }

        var current = text;
        for (var i = 0; i < edits.Count; i++)
        {
            current = Apply(current, edits[i]);
        }

        return current;
    }
}

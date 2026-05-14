using ProEdit.Markdown.Ast;

namespace ProEdit.Markdown;

public sealed class MarkdownIncrementalParser
{
    private readonly MarkdownOptions _options;

    public MarkdownIncrementalParser(MarkdownOptions? options = null)
    {
        _options = options ?? new MarkdownOptions();
    }

    public MarkdownDocument Parse(string text)
    {
        text ??= string.Empty;
        var parser = new MarkdownParser(_options);
        return parser.Parse(text.AsSpan());
    }

    public MarkdownDocument Update(
        MarkdownDocument? previous,
        string? oldText,
        string? newText,
        out IReadOnlyList<MarkdownTextEdit> edits)
    {
        oldText ??= string.Empty;
        newText ??= string.Empty;
        edits = MarkdownTextDiff.ComputeSingleEdit(oldText, newText);

        if (previous is null)
        {
            return Parse(newText);
        }

        if (edits.Count == 0)
        {
            return previous;
        }

        var maxId = MarkdownAstUtilities.GetMaxId(previous);
        var idProvider = new MarkdownNodeIdProvider(maxId + 1);
        var parser = new MarkdownParser(_options, idProvider);
        var current = parser.Parse(newText.AsSpan());

        current.Id = previous.Id;
        MarkdownAstUtilities.ReuseIds(previous, current, edits[0]);

        return current;
    }
}

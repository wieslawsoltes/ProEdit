using Vibe.Office.Html.Ast;

namespace Vibe.Office.Html;

public sealed class HtmlIncrementalParser
{
    private readonly HtmlOptions _options;

    public HtmlIncrementalParser(HtmlOptions? options = null)
    {
        _options = options ?? new HtmlOptions();
    }

    public HtmlDocument Parse(string text)
    {
        text ??= string.Empty;
        var parser = new HtmlAstParser(_options);
        return parser.Parse(text.AsSpan());
    }

    public HtmlDocument Update(
        HtmlDocument? previous,
        string? oldText,
        string? newText,
        out IReadOnlyList<HtmlTextEdit> edits)
    {
        oldText ??= string.Empty;
        newText ??= string.Empty;
        edits = HtmlTextDiff.ComputeSingleEdit(oldText, newText);

        if (previous is null)
        {
            return Parse(newText);
        }

        if (edits.Count == 0)
        {
            return previous;
        }

        var maxId = HtmlAstUtilities.GetMaxId(previous);
        var idProvider = new HtmlNodeIdProvider(maxId + 1);
        var parser = new HtmlAstParser(_options, idProvider);
        var current = parser.Parse(newText.AsSpan());

        current.Id = previous.Id;
        HtmlAstUtilities.ReuseIds(previous, current, edits[0]);

        return current;
    }
}

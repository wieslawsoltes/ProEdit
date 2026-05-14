using ProEdit.Documents;
using ProEdit.Html.Ast;

namespace ProEdit.Html;

public enum HtmlSyncSource
{
    Html,
    Document
}

public sealed class HtmlSyncState
{
    public HtmlSyncState(string htmlText, HtmlDocument ast, Document document)
    {
        HtmlText = htmlText ?? string.Empty;
        Ast = ast ?? throw new ArgumentNullException(nameof(ast));
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public string HtmlText { get; }
    public HtmlDocument Ast { get; }
    public Document Document { get; }
}

public sealed class HtmlDualViewSync
{
    private readonly HtmlOptions _options;
    private readonly HtmlIncrementalParser _parser;
    private readonly HtmlIncrementalSerializer _serializer;

    public HtmlDualViewSync(HtmlOptions? options = null)
    {
        _options = options ?? new HtmlOptions();
        _parser = new HtmlIncrementalParser(_options);
        _serializer = new HtmlIncrementalSerializer(_options);
    }

    public HtmlSyncState Initialize(string htmlText)
    {
        var text = htmlText ?? string.Empty;
        var ast = _parser.Parse(text);
        var document = HtmlDocumentConverter.FromHtml(text.AsSpan(), _options);
        return new HtmlSyncState(text, ast, document);
    }

    public HtmlSyncState ApplyHtmlEdit(HtmlSyncState state, string htmlText)
    {
        ArgumentNullException.ThrowIfNull(state);
        var text = htmlText ?? string.Empty;
        var ast = _parser.Update(state.Ast, state.HtmlText, text, out _);
        var document = HtmlDocumentConverter.FromHtml(text.AsSpan(), _options);
        return new HtmlSyncState(text, ast, document);
    }

    public HtmlSyncState ApplyDocumentEdit(HtmlSyncState state, Action<Document> edit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(edit);

        var document = DocumentClone.Clone(state.Document);
        edit(document);

        var html = HtmlDocumentConverter.ToHtml(document, _options);
        var ast = _parser.Parse(html);
        var update = _serializer.SerializeIncremental(ast, state.Ast, state.HtmlText);
        var nextText = update.Text;
        if (!string.Equals(state.HtmlText, html, StringComparison.Ordinal)
            && string.Equals(nextText, state.HtmlText, StringComparison.Ordinal))
        {
            nextText = html;
        }

        return new HtmlSyncState(nextText, ast, document);
    }

    public HtmlSyncState ResolveConflict(
        HtmlSyncState baseState,
        string? htmlEdit,
        Action<Document>? documentEdit,
        HtmlSyncSource lastWriter)
    {
        ArgumentNullException.ThrowIfNull(baseState);

        if (lastWriter == HtmlSyncSource.Html && htmlEdit is not null)
        {
            return ApplyHtmlEdit(baseState, htmlEdit);
        }

        if (lastWriter == HtmlSyncSource.Document && documentEdit is not null)
        {
            return ApplyDocumentEdit(baseState, documentEdit);
        }

        if (htmlEdit is not null)
        {
            return ApplyHtmlEdit(baseState, htmlEdit);
        }

        if (documentEdit is not null)
        {
            return ApplyDocumentEdit(baseState, documentEdit);
        }

        return baseState;
    }
}

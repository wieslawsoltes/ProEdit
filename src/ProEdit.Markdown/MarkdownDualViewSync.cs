using ProEdit.Documents;
using ProEdit.Markdown.Ast;

namespace ProEdit.Markdown;

public enum MarkdownSyncSource
{
    Markdown,
    Document
}

public sealed class MarkdownSyncState
{
    public MarkdownSyncState(string markdownText, MarkdownDocument ast, Document document)
    {
        MarkdownText = markdownText ?? string.Empty;
        Ast = ast ?? throw new ArgumentNullException(nameof(ast));
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public string MarkdownText { get; }
    public MarkdownDocument Ast { get; }
    public Document Document { get; }
}

public sealed class MarkdownDualViewSync
{
    private readonly MarkdownOptions _options;
    private readonly MarkdownIncrementalParser _parser;
    private readonly MarkdownIncrementalSerializer _serializer;

    public MarkdownDualViewSync(MarkdownOptions? options = null)
    {
        _options = options ?? new MarkdownOptions();
        _parser = new MarkdownIncrementalParser(_options);
        _serializer = new MarkdownIncrementalSerializer(_options);
    }

    public MarkdownSyncState Initialize(string markdownText)
    {
        var text = markdownText ?? string.Empty;
        var ast = _parser.Parse(text);
        var document = MarkdownAstConverter.ToDocument(ast, _options);
        return new MarkdownSyncState(text, ast, document);
    }

    public MarkdownSyncState ApplyMarkdownEdit(MarkdownSyncState state, string markdownText)
    {
        ArgumentNullException.ThrowIfNull(state);
        var text = markdownText ?? string.Empty;
        var ast = _parser.Update(state.Ast, state.MarkdownText, text, out _);
        var document = MarkdownAstConverter.ToDocument(ast, _options);
        return new MarkdownSyncState(text, ast, document);
    }

    public MarkdownSyncState ApplyDocumentEdit(MarkdownSyncState state, Action<Document> edit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(edit);

        var document = DocumentClone.Clone(state.Document);
        edit(document);
        var ast = MarkdownAstConverter.FromDocument(document, _options);
        var markdown = _serializer.Serialize(ast);
        return new MarkdownSyncState(markdown, ast, document);
    }

    public MarkdownSyncState ResolveConflict(
        MarkdownSyncState baseState,
        string? markdownEdit,
        Action<Document>? documentEdit,
        MarkdownSyncSource lastWriter)
    {
        ArgumentNullException.ThrowIfNull(baseState);

        if (lastWriter == MarkdownSyncSource.Markdown && markdownEdit is not null)
        {
            return ApplyMarkdownEdit(baseState, markdownEdit);
        }

        if (lastWriter == MarkdownSyncSource.Document && documentEdit is not null)
        {
            return ApplyDocumentEdit(baseState, documentEdit);
        }

        if (markdownEdit is not null)
        {
            return ApplyMarkdownEdit(baseState, markdownEdit);
        }

        if (documentEdit is not null)
        {
            return ApplyDocumentEdit(baseState, documentEdit);
        }

        return baseState;
    }
}

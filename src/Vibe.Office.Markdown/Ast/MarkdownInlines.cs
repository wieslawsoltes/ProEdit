namespace Vibe.Office.Markdown.Ast;

public sealed class MarkdownTextInline : MarkdownInline
{
    public MarkdownTextInline(MarkdownNodeId id, MarkdownTextSpan span, string text)
        : base(id, span)
    {
        Text = text ?? string.Empty;
    }

    public string Text { get; set; }
}

public sealed class MarkdownEmphasisInline : MarkdownInline
{
    public MarkdownEmphasisInline(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public bool IsStrong { get; set; }

    public List<MarkdownInline> Inlines { get; } = new();
}

public sealed class MarkdownStrikethroughInline : MarkdownInline
{
    public MarkdownStrikethroughInline(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public List<MarkdownInline> Inlines { get; } = new();
}

public sealed class MarkdownCodeInline : MarkdownInline
{
    public MarkdownCodeInline(MarkdownNodeId id, MarkdownTextSpan span, string code)
        : base(id, span)
    {
        Code = code ?? string.Empty;
    }

    public string Code { get; set; }
}

public sealed class MarkdownLinkInline : MarkdownInline
{
    public MarkdownLinkInline(MarkdownNodeId id, MarkdownTextSpan span, string url)
        : base(id, span)
    {
        Url = url ?? string.Empty;
    }

    public string Url { get; set; }

    public string? Title { get; set; }

    public List<MarkdownInline> Inlines { get; } = new();
}

public sealed class MarkdownImageInline : MarkdownInline
{
    public MarkdownImageInline(MarkdownNodeId id, MarkdownTextSpan span, string url)
        : base(id, span)
    {
        Url = url ?? string.Empty;
    }

    public string Url { get; set; }

    public string? Title { get; set; }

    public List<MarkdownInline> AltText { get; } = new();
}

public sealed class MarkdownHardBreakInline : MarkdownInline
{
    public MarkdownHardBreakInline(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }
}

public sealed class MarkdownSoftBreakInline : MarkdownInline
{
    public MarkdownSoftBreakInline(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }
}

public sealed class MarkdownHtmlInline : MarkdownInline
{
    public MarkdownHtmlInline(MarkdownNodeId id, MarkdownTextSpan span, string html)
        : base(id, span)
    {
        Html = html ?? string.Empty;
    }

    public string Html { get; set; }
}

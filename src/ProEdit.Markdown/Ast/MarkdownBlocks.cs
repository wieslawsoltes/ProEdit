namespace ProEdit.Markdown.Ast;

public enum MarkdownListKind
{
    Bullet,
    Ordered
}

public enum MarkdownTableAlignment
{
    None,
    Left,
    Center,
    Right
}

public sealed class MarkdownParagraphBlock : MarkdownBlock
{
    public MarkdownParagraphBlock(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public List<MarkdownInline> Inlines { get; } = new();
}

public sealed class MarkdownHeadingBlock : MarkdownBlock
{
    public MarkdownHeadingBlock(MarkdownNodeId id, MarkdownTextSpan span, int level)
        : base(id, span)
    {
        Level = level;
    }

    public int Level { get; set; }

    public List<MarkdownInline> Inlines { get; } = new();
}

public sealed class MarkdownBlockQuoteBlock : MarkdownBlock
{
    public MarkdownBlockQuoteBlock(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public List<MarkdownBlock> Blocks { get; } = new();
}

public sealed class MarkdownListBlock : MarkdownBlock
{
    public MarkdownListBlock(MarkdownNodeId id, MarkdownTextSpan span, MarkdownListKind kind)
        : base(id, span)
    {
        Kind = kind;
    }

    public MarkdownListKind Kind { get; set; }

    public int? StartNumber { get; set; }

    public bool IsTight { get; set; }

    public List<MarkdownListItemBlock> Items { get; } = new();
}

public sealed class MarkdownListItemBlock : MarkdownBlock
{
    public MarkdownListItemBlock(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public bool? IsTask { get; set; }

    public bool? TaskChecked { get; set; }

    public List<MarkdownBlock> Blocks { get; } = new();
}

public sealed class MarkdownCodeBlock : MarkdownBlock
{
    public MarkdownCodeBlock(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public string? Info { get; set; }

    public bool IsFenced { get; set; }

    public string Text { get; set; } = string.Empty;
}

public sealed class MarkdownThematicBreakBlock : MarkdownBlock
{
    public MarkdownThematicBreakBlock(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }
}

public sealed class MarkdownTableBlock : MarkdownBlock
{
    public MarkdownTableBlock(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public bool HasHeader { get; set; }

    public List<MarkdownTableRow> Rows { get; } = new();

    public List<MarkdownTableAlignment> Alignments { get; } = new();
}

public sealed class MarkdownTableRow : MarkdownNode
{
    public MarkdownTableRow(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public bool IsHeader { get; set; }

    public List<MarkdownTableCell> Cells { get; } = new();
}

public sealed class MarkdownTableCell : MarkdownNode
{
    public MarkdownTableCell(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public List<MarkdownInline> Inlines { get; } = new();
}

public sealed class MarkdownHtmlBlock : MarkdownBlock
{
    public MarkdownHtmlBlock(MarkdownNodeId id, MarkdownTextSpan span)
        : base(id, span)
    {
    }

    public string Html { get; set; } = string.Empty;
}

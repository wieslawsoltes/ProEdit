namespace Vibe.Office.Documents;

public sealed class FootnoteReferenceInline : Inline
{
    public int Id { get; }
    public TextStyleProperties? Style { get; set; }
    public string? StyleId { get; set; }

    public FootnoteReferenceInline(int id, TextStyleProperties? style = null)
    {
        Id = id;
        Style = style;
    }
}

public sealed class EndnoteReferenceInline : Inline
{
    public int Id { get; }
    public TextStyleProperties? Style { get; set; }
    public string? StyleId { get; set; }

    public EndnoteReferenceInline(int id, TextStyleProperties? style = null)
    {
        Id = id;
        Style = style;
    }
}

public sealed class CommentRangeStartInline : Inline
{
    public int Id { get; }

    public CommentRangeStartInline(int id)
    {
        Id = id;
    }
}

public sealed class CommentRangeEndInline : Inline
{
    public int Id { get; }

    public CommentRangeEndInline(int id)
    {
        Id = id;
    }
}

public sealed class CommentReferenceInline : Inline
{
    public int Id { get; }
    public TextStyleProperties? Style { get; set; }
    public string? StyleId { get; set; }

    public CommentReferenceInline(int id, TextStyleProperties? style = null)
    {
        Id = id;
        Style = style;
    }
}

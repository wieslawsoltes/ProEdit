namespace Vibe.Office.Documents;

public sealed class FootnoteDefinition
{
    public int Id { get; set; }
    public List<Block> Blocks { get; } = new List<Block>();

    public FootnoteDefinition(int id)
    {
        Id = id;
    }
}

public sealed class EndnoteDefinition
{
    public int Id { get; set; }
    public List<Block> Blocks { get; } = new List<Block>();

    public EndnoteDefinition(int id)
    {
        Id = id;
    }
}

public sealed class CommentDefinition
{
    public int Id { get; set; }
    public string? Author { get; set; }
    public string? Initials { get; set; }
    public DateTime? Date { get; set; }
    public int? ParentId { get; set; }
    public int? ThreadId { get; set; }
    public bool IsResolved { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedDate { get; set; }
    public List<Block> Blocks { get; } = new List<Block>();

    public CommentDefinition(int id)
    {
        Id = id;
    }
}

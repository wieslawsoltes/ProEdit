namespace Vibe.Office.Documents;

public sealed class BookmarkStartInline : Inline
{
    public int Id { get; }
    public string Name { get; }

    public BookmarkStartInline(int id, string name)
    {
        Id = id;
        Name = name ?? string.Empty;
    }
}

public sealed class BookmarkEndInline : Inline
{
    public int Id { get; }

    public BookmarkEndInline(int id)
    {
        Id = id;
    }
}

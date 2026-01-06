namespace Vibe.Office.Documents;

public enum ListKind
{
    None,
    Bullet,
    Numbered
}

public sealed class ListInfo
{
    public ListKind Kind { get; }
    public int Level { get; }
    public int? ListId { get; }
    public ListNumberFormat? NumberFormat { get; set; }
    public string? LevelText { get; set; }
    public string? BulletSymbol { get; set; }
    public int? StartAt { get; set; }
    public float? LeftIndent { get; set; }
    public float? HangingIndent { get; set; }
    public float? TabStop { get; set; }

    public ListInfo(ListKind kind, int level = 0, int? listId = null)
    {
        Kind = kind;
        Level = Math.Max(0, level);
        ListId = listId;
    }

    public ListInfo Clone()
    {
        return new ListInfo(Kind, Level, ListId)
        {
            NumberFormat = NumberFormat,
            LevelText = LevelText,
            BulletSymbol = BulletSymbol,
            StartAt = StartAt,
            LeftIndent = LeftIndent,
            HangingIndent = HangingIndent,
            TabStop = TabStop
        };
    }
}

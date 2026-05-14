namespace ProEdit.Documents;

public sealed class ListLevelDefinition
{
    public int Level { get; }
    public ListNumberFormat Format { get; set; } = ListNumberFormat.Decimal;
    public string? LevelText { get; set; }
    public string? BulletSymbol { get; set; }
    public int StartAt { get; set; } = 1;
    public float? LeftIndent { get; set; }
    public float? HangingIndent { get; set; }
    public float? TabStop { get; set; }

    public ListLevelDefinition(int level)
    {
        Level = Math.Max(0, level);
    }

    public ListLevelDefinition Clone()
    {
        return new ListLevelDefinition(Level)
        {
            Format = Format,
            LevelText = LevelText,
            BulletSymbol = BulletSymbol,
            StartAt = StartAt,
            LeftIndent = LeftIndent,
            HangingIndent = HangingIndent,
            TabStop = TabStop
        };
    }
}

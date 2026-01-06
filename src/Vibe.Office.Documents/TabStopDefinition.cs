namespace Vibe.Office.Documents;

public enum TabAlignment
{
    Left,
    Center,
    Right,
    Decimal
}

public enum TabLeader
{
    None,
    Dot,
    Hyphen,
    Underscore
}

public sealed class TabStopDefinition : IComparable<TabStopDefinition>
{
    public float Position { get; set; }
    public TabAlignment Alignment { get; set; } = TabAlignment.Left;
    public TabLeader Leader { get; set; } = TabLeader.None;

    public TabStopDefinition(float position)
    {
        Position = position;
    }

    public TabStopDefinition Clone()
    {
        return new TabStopDefinition(Position)
        {
            Alignment = Alignment,
            Leader = Leader
        };
    }

    public int CompareTo(TabStopDefinition? other)
    {
        if (other is null)
        {
            return 1;
        }

        return Position.CompareTo(other.Position);
    }
}
